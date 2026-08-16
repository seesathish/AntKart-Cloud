# AKS Guide

**Purpose:** How the services are containerized and run on a managed Kubernetes (AKS) cluster — packaging, image delivery, ingress, health management, and workload-identity-based access to cloud resources with no stored secrets.

This guide is built one area at a time, following the same rhythm as the other build guides. **Containerization, the AKS cluster, per-service workload identity, Helm deployment of all six services, and ingress with cert-manager TLS are complete — verified end to end through the public HTTPS endpoint — and documented below.** Only base-image hardening and GitOps delivery remain placeholders. For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md); for the decisions behind the cluster shape, see [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

---

## Container Strategy

Every deployable service is packaged as a container image built the same way, so the fleet is uniform to build, scan, and run.

**Multi-stage builds.** Each `Dockerfile` uses a multi-stage build: a .NET 9 **SDK** stage restores and publishes the service, and a smaller .NET 9 **ASP.NET runtime** stage carries only the published output. Restore is performed before the full source is copied so Docker's layer cache is reused across builds when only source — not project references — changes. The build context is the **repository root** (the shared `AK.BuildingBlocks` project and `nuget.config` live above each service folder), so images are built with `-f <service>/Dockerfile .` from the root.

**Non-root, on port 8080.** Each runtime image sets `USER $APP_UID` (the .NET base image's non-root user) before the entry point, so containers never run as root. Because a non-root user cannot bind privileged ports (below 1024), every service listens on **port 8080** — the .NET base image's default (`ASPNETCORE_HTTP_PORTS=8080`) — and each `Dockerfile` declares `EXPOSE 8080`. Kubernetes Services target port 8080.

**Health and readiness endpoints.** Every service exposes the three health surfaces provided by the shared building blocks, shaped for orchestrator probes (see the [Observability design](../guides/observability-concepts.md) and the resilience/health step of the [Cloud Migration Guide](cloud-migration-guide.md)):

- **`GET /health/live`** — shallow liveness. Makes no external calls, so a dependency blip cannot trigger a restart storm. This is the **liveness probe** target.
- **`GET /health/ready`** — tolerant readiness (a degraded shared dependency still returns HTTP 200). This is the **readiness probe** target.
- `GET /health/deps` — detailed per-dependency diagnostics for humans and dashboards; **not** a probe target.

The Kubernetes probe definitions that consume `/health/live` and `/health/ready` are configured by the Helm chart (see [Deploying the Services](#deploying-the-services-helm)).

**Serverless notification is not containerized.** `AK.Notification.Functions` is deployed as an Azure Function (Event Grid-triggered) and is **not** part of the container fleet — it is delivered through the serverless path, not the cluster.

### The `.dockerignore` rationale

A repository-root `.dockerignore` keeps the build context small and clean. It matters for two reasons:

- **Build speed and cache stability.** With the build context at the repository root, everything not excluded is sent to the Docker daemon on every build. Excluding build output (`bin/`, `obj/`), IDE state, `node_modules`, and test artifacts keeps the context small so builds start quickly and caching stays effective.
- **Keeping local configuration and secrets out of images.** Development-only configuration (`appsettings.Development.json`), local user files, and any local secret material must never be copied into an image layer — image layers are inspectable and are pushed to a registry. Runtime configuration is supplied from environment variables and Key Vault instead (see [Container Configuration](container-configuration.md)), never baked in at build time.

---

## Build and Push to the Azure Container Registry

Images are pushed to the platform's Azure Container Registry (see the [Infrastructure Guide](infrastructure-guide.md)). Access is by Microsoft Entra identity — the registry has its admin account disabled — so authentication uses the signed-in identity, with **no registry username or password**.

```bash
# Authenticate to the registry with your Entra identity (no registry credentials).
az acr login --name acrantkartdev

# Build and push one service (build context is the repository root).
docker build -f AK.Products/AK.Products.API/Dockerfile \
  -t acrantkartdev.azurecr.io/antkart/products:<tag> .
docker push acrantkartdev.azurecr.io/antkart/products:<tag>
```

Alternatively, `az acr build -r acrantkartdev -t antkart/products:<tag> -f AK.Products/AK.Products.API/Dockerfile .` builds server-side in the registry and pushes in one step.

At run time the cluster pulls images with **no registry credentials**: the cluster's kubelet identity is granted the built-in **AcrPull** role on the registry, so image pulls are authorized by managed identity. That role assignment is provisioned with the cluster (see [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md)).

### Image naming and tagging convention

Images live under the `antkart/` namespace in the registry, one repository per deployable service:

`acrantkartdev.azurecr.io/antkart/<service>:<tag>`

Each image is tagged with the **short Git commit SHA** of the build for immutable, traceable provenance, plus a moving environment tag (for example `dev`) that points at the latest image promoted to that environment. Deployments reference the immutable SHA tag so a rollout is always tied to an exact commit.

---

## Naming Conventions: In-Cluster Services vs Azure Resources

Two naming schemes are used deliberately, and they are not interchangeable:

- **In-cluster Kubernetes Service names use the short `ak-` prefix.** These are the DNS names services use to reach each other inside the cluster (for example, the gateway's downstream routes and the Order service's catalogue calls resolve to these).
- **Azure resources keep the `antkart-` prefix** (resource groups, registry, Key Vault, Service Bus, and so on), consistent with the infrastructure-as-code naming.

The complete mapping of service, `Dockerfile`, image repository, and in-cluster Service name:

| Service | Dockerfile | Image repository | In-cluster Service |
|---------|------------|------------------|--------------------|
| Products (REST) | `AK.Products/AK.Products.API/Dockerfile` | `antkart/products` | `ak-products` |
| ShoppingCart (REST) | `AK.ShoppingCart/AK.ShoppingCart.API/Dockerfile` | `antkart/cart` | `ak-cart` |
| Order (REST) | `AK.Order/AK.Order.API/Dockerfile` | `antkart/order` | `ak-order` |
| Payments (REST) | `AK.Payments/AK.Payments.API/Dockerfile` | `antkart/payments` | `ak-payments` |
| Gateway | `AK.Gateway/AK.Gateway.API/Dockerfile` | `antkart/gateway` | `ak-gateway` |
| Discount (gRPC) | `AK.Discount/AK.Discount.Grpc/Dockerfile` | `antkart/discount` | `ak-discount` |

The runtime configuration each of these services requires — connection strings, endpoints, and the values that must come from environment or Key Vault rather than committed configuration — is catalogued in [Container Configuration](container-configuration.md), which is the source for the Helm values.

---

## The AKS Cluster

The cluster `aks-antkart-dev` is provisioned by the `aks` Terraform module and its dev unit (see the [Infrastructure map](../../infrastructure/README.md)). Its shape is a deliberate set of dev-appropriate choices; the reasoning for each is recorded in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

| Aspect | As-built | Why |
|--------|----------|-----|
| Kubernetes version | `1.35`, pinned | A pinned version makes provisioning reproducible. AKS retires versions over time, so the value is chosen from those marked `KubernetesOfficial` (see [Troubleshooting](#troubleshooting)). |
| Control-plane SKU tier | `Free` | No uptime SLA but no control-plane cost — right for a disposable dev cluster. Production selects `Standard` for the financially-backed SLA. |
| Node pool | A single **system** pool, `2 × Standard_D2s_v3`, **autoscaling disabled** | One fixed-size pool keeps dev cost predictable and the setup simple. Production splits system/user pools and enables autoscaling. |
| Node subnet | The `aks` subnet, `10.0.0.0/22` | The large subnet the networking module sized for the cluster's nodes. |
| Networking | **Azure CNI Overlay** — plugin `azure`, mode `overlay`, policy `azure` | Overlay draws pod IPs from an overlay CIDR so pods do **not** consume VNet address space; network policy is enforced by the Azure CNI itself (no separate Calico add-on). |
| Pod / service CIDRs | pod `10.244.0.0/16`, service `10.245.0.0/16`, DNS `10.245.0.10` | Chosen not to overlap the VNet (`10.0.0.0/16`) or each other. |
| Control-plane identity | `SystemAssigned` | Azure manages its lifecycle with the cluster. The nodes get a separate kubelet identity. |
| Image pulls | Kubelet identity granted **AcrPull** on the registry | Nodes pull images with **no `imagePullSecret`** — verified by a successful pull with no registry credentials. |
| OIDC issuer + workload identity | **Enabled at creation** | Both are required for federated workload identity. Enabling them after creation forces a cluster update, so they are set at creation time. |
| Authorization | **Azure RBAC** for Kubernetes enabled; local accounts left enabled | `kubectl` access is granted with an Azure role (see [Operator Access](#operator-access)). Local accounts are intentionally left enabled for now so we cannot lock ourselves out. |
| Monitoring | OMS agent → existing Log Analytics workspace | Reuses the platform's existing workspace, with managed-identity auth. |

---

## Operator Access

Because the cluster uses **Azure RBAC for Kubernetes authorization**, being able to reach the API server is not enough — an **Azure role** is required to run `kubectl`, and the Entra-enabled cluster needs `kubelogin` to convert the kubeconfig to a token-based login.

**1. Grant yourself a cluster RBAC role** (scoped to the cluster, not the subscription):

```bash
CLUSTER_ID=$(az aks show -g rg-antkart-dev-eastus -n aks-antkart-dev --query id -o tsv)

az role assignment create \
  --assignee "<your-entra-object-id>" \
  --role "Azure Kubernetes Service RBAC Cluster Admin" \
  --scope "$CLUSTER_ID"
```

**2. Fetch credentials and convert them for Entra login:**

```bash
az aks get-credentials -g rg-antkart-dev-eastus -n aks-antkart-dev

az aks install-cli                          # installs kubectl and kubelogin
kubelogin convert-kubeconfig -l azurecli    # REQUIRED for an Entra-enabled cluster

kubectl get nodes                           # first call triggers an Entra sign-in
```

`kubelogin` is **required** here: without converting the kubeconfig, `kubectl` cannot obtain an Entra token for the cluster and every call fails to authenticate. Note that `az aks install-cli` updates your `PATH` — **open a new shell** so `kubectl` and `kubelogin` resolve.

---

## Workload Identity

Pods reach Azure resources (Key Vault, Service Bus, Event Grid) with **no stored secret**, using the same `DefaultAzureCredential` line the services already run locally. This is delivered by the `workload-identity` module and its dev unit (see the [Infrastructure map](../../infrastructure/README.md)).

**The model.** For each service there is a **user-assigned managed identity** (`id-ak-<service>-dev`) with a **federated identity credential** that trusts the cluster's OIDC issuer for exactly one Kubernetes ServiceAccount:

- **Subject:** `system:serviceaccount:antkart:ak-<service>` — this is **exact-match and case-sensitive**. A mismatch (wrong namespace, wrong ServiceAccount name, wrong case) fails as `AADSTS70021` / "workload options are not fully configured", not as a permission error.
- **Audience:** `api://AzureADTokenExchange` — the fixed Entra token-exchange audience the workload-identity webhook expects.

### Least-privilege role matrix

Each identity receives only the data-plane roles its service needs. Cosmos DB, PostgreSQL, and Redis are reached via **connection strings stored in Key Vault**, so access to them is granted transitively through **Key Vault Secrets User** rather than a data-plane role on those stores.

| Service (ServiceAccount) | Managed identity | Roles granted → scope |
|--------------------------|------------------|-----------------------|
| `ak-products` | `id-ak-products-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| `ak-cart` | `id-ak-cart-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| `ak-order` | `id-ak-order-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| `ak-payments` | `id-ak-payments-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| `ak-discount` | `id-ak-discount-dev` | Key Vault Secrets User → vault |
| `ak-gateway` | `id-ak-gateway-dev` | Key Vault Secrets User → vault |

### Pod wiring

Two pieces connect a pod to its identity — an **annotation on the ServiceAccount** carrying the identity's client id, and a **label on the pod template** opting the pod into the webhook:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: ak-products                     # matches the federated-credential subject
  namespace: antkart
  annotations:
    azure.workload.identity/client-id: "<client_id of id-ak-products-dev>"
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ak-products
  namespace: antkart
spec:
  template:
    metadata:
      labels:
        azure.workload.identity/use: "true"    # MUST be on the pod template, not only the Deployment
    spec:
      serviceAccountName: ak-products
      containers:
        - name: ak-products
          image: acrantkartdev.azurecr.io/antkart/products:<tag>
```

With the annotation and label present, the webhook injects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_FEDERATED_TOKEN_FILE`, and `AZURE_AUTHORITY_HOST` into the pod, and `DefaultAzureCredential` resolves to `WorkloadIdentityCredential`. **Application code is unchanged.** The client id for each service comes from the `workload-identity` module's `identities` output.

**Verified behaviour.** A pod deployed **without** this wiring crashed: `DefaultAzureCredential` fell back to IMDS and failed with "Multiple user assigned identities exist" (the node carries several identities, and IMDS cannot pick one). The **same image with the annotation and label** reached `Running` and read Key Vault successfully — deterministically selecting the one federated identity for that service, with no secret involved.

---

## Deploying the Services (Helm)

All six services deploy through a **single parameterised Helm chart** (`deploy/helm/antkart-service`) instantiated once per service with a per-service values file (`deploy/helm/values/<service>.yaml`). The chart renders, per service, the workload-identity `ServiceAccount` (annotated with the identity's client id) and `Deployment` (pod template labelled `azure.workload.identity/use`; see [Pod wiring](#pod-wiring) above), a `ClusterIP` `Service` on 8080, and a `ConfigMap` of non-secret configuration. The full chart reference is in [deploy/helm/README](../../deploy/helm/README.md).

Install or upgrade one service (idempotent):

```bash
helm upgrade --install ak-products deploy/helm/antkart-service \
  -n antkart -f deploy/helm/values/products.yaml
```

### Startup probe — Key Vault at boot must not restart-loop the pod

Each service loads its secrets from Key Vault during host startup, which can take several seconds before Kestrel binds. A **`startupProbe`** gates the liveness and readiness probes so they do not run until the process has finished booting (`failureThreshold × periodSeconds` allows up to ~150s). Without it, the liveness probe would fire mid-boot, fail, and Kubernetes would kill and restart the pod in a loop. Once the startup probe first succeeds, **liveness** hits shallow `/health/live` (no external calls) and **readiness** hits tolerant `/health/ready`. Resource requests are sized to the node pool (see [the cluster shape](#the-aks-cluster)) so all six services fit with headroom.

### AK.Discount — h2c gRPC probes

`ak-discount` serves **HTTP/2 cleartext (h2c)** only, so an HTTP/1.1 `httpGet` probe would be rejected. Its chart therefore uses **TCP** probes, and its Service port is named `grpc` with `appProtocol: grpc`. It is **internal-only** and is never exposed through the ingress — a public HTTP edge cannot carry cleartext gRPC, and only Products calls it in-cluster.

### Image path decoupled from the in-cluster name

The chart derives the image from an optional `image.name`, falling back to `serviceName`. They are deliberately separate: **`serviceName` drives the ServiceAccount name (`ak-<serviceName>`), which must exactly match the federated identity subject `system:serviceaccount:antkart:ak-<service>`** — so it cannot be renamed to chase a registry repository, or workload identity silently breaks. The image path, by contrast, must match the repository that actually exists in ACR. The cart service is the case in point: it runs as **`ak-cart`** (ServiceAccount and Service) while pulling **`antkart/shoppingcart`** (image), by setting `image.name: shoppingcart` in its values file.

### Gateway health endpoints must bypass Ocelot

The gateway needs one special piece of wiring. Ocelot's middleware is **terminal** — it treats any path that reaches it as a proxy request and returns 404 for anything without a matching downstream route. Mapping the gateway's own health endpoints "before" `UseOcelot()` is **not** sufficient, because `WebApplication` defers endpoint execution to the end of the pipeline: Ocelot short-circuits first, so `/health/live` returns 404 and the probe restart-loops the gateway pod. The fix runs Ocelot **only for non-`/health` paths** (via `MapWhen`), so health requests fall through to endpoint routing and are served. The gateway's liveness and readiness are both **shallow self-checks** — they must never depend on a downstream service, or one failing service would restart the gateway.

---

## Cost and Idle Management

The node pool bills **hourly** whenever the cluster is running. For idle periods, stop and start the cluster rather than destroying it:

```bash
az aks stop  -g rg-antkart-dev-eastus -n aks-antkart-dev   # stops node billing
az aks start -g rg-antkart-dev-eastus -n aks-antkart-dev
```

---

## Troubleshooting

Issues encountered provisioning and operating this cluster, and how to resolve them:

- **Kubernetes version unavailable / LTS-only.** AKS continually retires versions. A version offered **only** under `AKSLongTermSupport` requires an LTS-tier subscription and will otherwise be rejected. List what a region actually offers and pick one marked `KubernetesOfficial`:
  ```bash
  az aks get-versions --location eastus -o table
  ```
- **VM size unavailable, or the wrong CPU architecture.** Subscription SKU allow-lists vary by region, so the intended VM size may be unavailable. Watch the **architecture**, not just availability: the available `b*ps_v2` sizes are **ARM64**, which will **not** run amd64-built images. Confirm both availability and architecture before choosing a size.
- **Provider lock drift across Terragrunt units.** Each Terragrunt unit pins its providers in its **own** `.terraform.lock.hcl`. Adding a provider to the shared root config does not update the units automatically — run `terragrunt init -upgrade` in each affected unit and commit the refreshed lock files.
- **`kubectl` cannot authenticate after `install-cli`.** `az aks install-cli` changes `PATH`; open a **new shell** so `kubectl`/`kubelogin` resolve, and ensure `kubelogin convert-kubeconfig -l azurecli` has been run for this Entra-enabled cluster.
- **Federated token rejected (`AADSTS70021`).** The ServiceAccount subject does not match the federated credential. The subject `system:serviceaccount:antkart:ak-<service>` is exact-match and case-sensitive — check the namespace, the ServiceAccount name, and the pod-template label.
- **Stale image after pushing to the same tag.** With a mutable tag (e.g. `dev`) and `imagePullPolicy: IfNotPresent`, a node that has already cached that tag keeps serving the **old** image after a new one is pushed to the same tag — the ConfigMap/manifest change deploys but the code does not. Use an **immutable tag** (the commit SHA) so every rollout pulls a distinct image; this is adopted with the delivery pipeline. (As a stop-gap, delete the pod to force a fresh pull, or roll the tag.)
- **No shell in the runtime image.** The images ship no shell or tooling, so `kubectl exec ... -- curl` does not work. Use an **ephemeral debug container** (`kubectl debug`) or a throwaway debug pod carrying the tools you need.

---

## Ingress and TLS

External traffic reaches the platform through a single HTTPS entry point: an NGINX ingress controller terminates TLS and forwards to the **gateway only**. The five other services stay `ClusterIP` and are reached through the gateway's Ocelot routes.

> **Target-state edge (ADR-020).** The internal ingress described here is the **internal-routing layer** of a two-gateway model. In the target state, **Azure API Management** sits in front of it as the managed external edge — owning TLS termination, JWT validation, rate limiting and quotas, subscription keys/products, a developer portal, and request/response transformation — while this ingress continues to route to services inside the cluster. The two are **sequenced layers, not competing gateways**: the internal ingress is the prerequisite, delivered first; APIM is added in front of it afterwards. See [ADR-020](../adr/ADR-020-api-management-managed-edge-gateway.md).

### Gateway-only exposure (the API-gateway pattern)

Only **`ak-gateway`** has an Ingress. Products, ShoppingCart, Order, Payments, and Discount remain internal `ClusterIP` services with no Ingress (the chart's `ingress.enabled` defaults to `false`, so they never render one). All external calls enter through the gateway, whose Ocelot routes fan out to the internal services over cluster DNS. This keeps one auditable, rate-limited, JWT-validating front door and a minimal attack surface — the internal services are never directly reachable from outside the cluster. In particular **`ak-discount` (h2c gRPC) is never exposed**: it is an internal dependency called by Products, and putting cleartext gRPC behind a public HTTP ingress would neither work nor be safe.

The gateway ingress routes `/` (all paths) to `ak-gateway:8080`. The gateway then serves its real upstream routes internally — all under `/gateway/*`:

`/gateway/products` · `/gateway/products/{everything}` · `/gateway/cart/{everything}` · `/gateway/orders` · `/gateway/orders/{everything}` · `/gateway/payments/{everything}` · `/gateway/health/{products|cart|orders|payments}` — plus the gateway's own `/health/*`.

### Ingress controller — self-managed ingress-nginx (chosen)

Two options were considered:

- **AKS managed NGINX (application routing add-on)** — less to operate, and it can be provisioned as code in the `aks` Terraform module (`web_app_routing { ... }`). But it is Azure-specific.
- **Self-managed `ingress-nginx` via Helm (chosen)** — the same controller, Ingress resources, and cert-manager flow run **identically on AKS and EKS**, so the entire ingress/TLS layer is portable and transfers unchanged to the planned AWS deployment. The small extra operational cost (we install/upgrade the chart ourselves) is worth the cloud-agnostic Kubernetes layer.

Because it is Helm-installed, the `aks` Terraform module is unchanged. (If the managed add-on were preferred instead, it would be enabled via a `web_app_routing` block in `modules/aks/main.tf`, not clicked on in the portal.)

### cert-manager and Let's Encrypt

[cert-manager](https://cert-manager.io) automates certificate issuance. Two `ClusterIssuer`s are defined ([deploy/cert-manager](../../deploy/cert-manager/)), both using the ACME **HTTP-01** solver against the `nginx` class:

- **`letsencrypt-staging`** — generous rate limits, **untrusted** root (browsers warn; use `curl -k`).
- **`letsencrypt-prod`** — browser-trusted, **strict** weekly rate limits.

**Always validate on staging first.** Let's Encrypt production limits are strict (e.g. duplicate-certificate and per-domain weekly caps); a misconfigured Ingress that retries in a loop can exhaust them and lock the account out for about a week. Only after the *same* setup issues a `Ready` certificate on staging do you switch the Ingress to production.

The Ingress carries a `cert-manager.io/cluster-issuer` annotation and a `tls` block naming a Secret; cert-manager watches these, runs the HTTP-01 challenge, and writes the issued cert/key into that Secret, which the controller then uses to terminate TLS.

### Hostname — custom domain `api.antkart.in` (nip.io as the zero-DNS fallback)

The platform is served at the **custom domain `api.antkart.in`** — a GoDaddy **A record** points it at the ingress controller's LoadBalancer public IP (`20.246.197.150`), and cert-manager issues a **trusted Let's Encrypt production** certificate for it. Where no domain is available, **[nip.io](https://nip.io) wildcard DNS** (`<public-ip>.nip.io`, which resolves to that IP with no DNS setup) is the drop-in fallback — the same Ingress/cert-manager wiring applies unchanged; only the `ingress.host` value differs.

> **Delivered state is GitOps-driven.** The live gateway host and issuer are set **in Git** — the `ak-gateway` Argo CD Application (`ingress.host=api.antkart.in`) and `deploy/helm/values/gateway.yaml` (`clusterIssuer: letsencrypt-prod`) — and reconciled by Argo CD with self-heal on. A `helm upgrade --set ...` would be **reverted** by self-heal. The runbook below is the **first-time bootstrap / non-GitOps path**; to change the host or issuer on the running platform, edit Git and let Argo CD apply it (see the [GitOps Guide](gitops-guide.md)).

### Runbook

Run these in order. Substitute `PUBLIC_IP` once the controller has one.

```bash
# 1. Install ingress-nginx (self-managed) and cert-manager's Helm repos
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo add jetstack https://charts.jetstack.io
helm repo update

helm upgrade --install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace \
  --set controller.service.externalTrafficPolicy=Local

# 2. Get the controller's public IP — wait until EXTERNAL-IP is no longer <pending>
kubectl -n ingress-nginx get svc ingress-nginx-controller -w
PUBLIC_IP=<paste EXTERNAL-IP here>
HOST=$PUBLIC_IP.nip.io

# 3. Install cert-manager (with CRDs), then the issuers (edit the email in each first)
helm upgrade --install cert-manager jetstack/cert-manager \
  --namespace cert-manager --create-namespace \
  --set crds.enabled=true

kubectl apply -f deploy/cert-manager/cluster-issuer-staging.yaml
kubectl apply -f deploy/cert-manager/cluster-issuer-prod.yaml

# 4. Enable the gateway ingress with the nip.io host (STAGING issuer by default)
helm upgrade --install ak-gateway deploy/helm/antkart-service \
  -n antkart -f deploy/helm/values/gateway.yaml \
  --set ingress.enabled=true \
  --set ingress.host=$HOST

# 5. Verify
kubectl -n antkart get ingress ak-gateway
kubectl -n antkart get certificate,certificaterequest,order,challenge
kubectl -n antkart describe certificate ak-gateway-tls   # watch Status/Events
curl -k https://$HOST/health/live                        # staging cert is untrusted -> -k; expect 200 "Healthy"
```

> **PowerShell note.** These are bash examples. In native PowerShell `curl` is an alias for `Invoke-WebRequest`, so use **`curl.exe -k https://$HOST/health/live`** for the `-k` (insecure-TLS) flag to take effect. See [Operations Command Reference → Gotchas](operations-command-reference.md#j-gotchas-and-powershell-notes).

**Switch to production** once staging shows a `Ready` certificate. On the delivered (GitOps) platform this is a **Git change** — `deploy/helm/values/gateway.yaml` already sets `clusterIssuer: letsencrypt-prod` and the `ak-gateway` Application sets `ingress.host: api.antkart.in`, and Argo CD reconciles it. The manual equivalent (bootstrap / non-GitOps) is:

```bash
helm upgrade ak-gateway deploy/helm/antkart-service -n antkart -f deploy/helm/values/gateway.yaml \
  --set ingress.enabled=true --set ingress.host=$HOST \
  --set ingress.clusterIssuer=letsencrypt-prod
kubectl -n antkart delete secret ak-gateway-tls          # force a fresh, trusted cert
# then curl WITHOUT -k — the certificate is browser-trusted:
curl https://$HOST/health/live
```

### Verify the certificate and the `api.antkart.in` endpoint

On the running platform (host `api.antkart.in`, prod issuer), confirm the certificate issued and the endpoint serves a trusted cert:

```bash
kubectl -n antkart get ingress ak-gateway \
  -o jsonpath='{.metadata.annotations.cert-manager\.io/cluster-issuer}{"\n"}'   # -> letsencrypt-prod
kubectl -n antkart get certificate ak-gateway-tls                                # READY column must be True
kubectl -n antkart get certificate ak-gateway-tls \
  -o jsonpath='{.spec.dnsNames[*]}{"\n"}'                                        # -> api.antkart.in
curl https://api.antkart.in/health/live                                          # 200 "Healthy", NO -k (trusted prod cert)
```
- The first two commands confirm the live annotation is `letsencrypt-prod` and the `ak-gateway-tls` Certificate is `Ready=True` for `api.antkart.in`. The final `curl` (no `-k`) proves the browser-trusted production certificate terminates TLS — a `-k`-free 200 is the whole point of the prod cutover. (PowerShell: `curl.exe https://api.antkart.in/health/live`.)

### Troubleshooting — certificate stays `Pending`

cert-manager issues through a chain: **Certificate → CertificateRequest → Order → Challenge**. Walk it to find the stuck step:

```bash
kubectl -n antkart describe certificate ak-gateway-tls
kubectl -n antkart get challenge
kubectl -n antkart describe challenge <name>   # HTTP-01 validation detail
```

Common causes:

- **DNS not resolving to the controller** — `nslookup $HOST` must return `PUBLIC_IP`. If `ingress.host` was built from the wrong IP, the HTTP-01 challenge can't reach the cluster.
- **Controller has no public IP** — `kubectl -n ingress-nginx get svc` still shows `<pending>` (Azure LB not provisioned yet); wait, or check the service events.
- **Challenge 404 / not served** — the challenge path `/.well-known/acme-challenge/...` must reach the controller; a wrong `ingressClassName` or a conflicting redirect can block it (nginx serves the ACME path even with `ssl-redirect` on).
- **Invalid issuer email** — the placeholder `email:` in the ClusterIssuer must be a real, syntactically valid address.
- **Production rate limit** — if you jumped to `letsencrypt-prod` and got locked out, go back to staging; the account is limited for up to a week.

### Troubleshooting — the ingress is unreachable (custom NSG)

If the ingress controller has a public IP but every request — `curl` **and** the ACME HTTP-01 challenge — times out, the cause is likely the **custom NSG on a bring-your-own VNet**. With a customer-managed NSG, AKS does **not** automatically add the LoadBalancer allow rules for a public Service, so client traffic arrives with the `Internet` service tag, matches no allow rule, and is dropped by the deny-all baseline. The `AzureLoadBalancer` service tag covers only Azure's **health probes**, not client traffic — so the load balancer has a public IP but silently drops every request.

The fix is to add inbound allow rules for **80 and 443 from the `Internet` tag**, and **only on the subnet hosting the ingress controller** (the `aks` subnet). This is driven by the networking module's per-subnet `allow_internet_ingress` flag (see [infrastructure/README](../../infrastructure/README.md)). Port **80 must stay open to the `Internet` tag** — Let's Encrypt validates HTTP-01 from many rotating IPs with no published allowlist, so it cannot be narrowed to a fixed source range.

---

## GitOps delivery (Argo CD)

The manual `helm upgrade` flow above is how the cluster was first deployed and remains valid, but the platform is **now Git-driven**: **Argo CD** runs in the cluster, watches this repository, and reconciles the live state to the **same** generic Helm chart per service. Deploying a change is a `git push`; Argo CD renders the identical chart and applies the result. Adoption was done manual-sync-first for a safe, observable cutover of the already-running services (the only diff was Argo CD's tracking-id annotations), and a `replicaCount` change was proven to roll out through Git alone.

Full concept, structure, install, and adoption runbook: **[GitOps Guide](gitops-guide.md)** (manifests: [deploy/argocd/README](../../deploy/argocd/README.md)). The CI/CD platform and its authentication model are decided in [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md); the delivery pipeline is documented in the [DevOps Guide](devops-guide.md) as it is built.

---

## Still to Come

The section below covers work that is **not yet delivered**. It is a placeholder describing scope only; nothing here should be treated as done.

### 🚧 Base-image hardening _(placeholder)_

Moving the runtime image from the standard ASP.NET runtime to a hardened, minimal base and publishing an organisation-owned base image, per the intent in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).
