# Operations Command Reference

Every command used to build, inspect, troubleshoot, and operate the AntKart platform, in one place — with **each parameter explained**, not just listed. The goal: run the platform from the command line without re-deriving anything.

This reference is the *how to run it* companion to the deeper guides. Where a concept needs explaining, it links out rather than duplicating: the [AKS Guide](aks-guide.md) (cluster, workload identity, ingress/TLS, troubleshooting), the [Infrastructure Guide](infrastructure-guide.md) (per-resource provisioning), [Container Configuration](container-configuration.md) (per-service config keys), [deploy/helm/README](../../deploy/helm/README.md) (chart reference), and [deploy/cert-manager/README](../../deploy/cert-manager/README.md) (issuers).

## How to use this document

Commands use **`<PLACEHOLDER>`** for environment-specific values. Substitute the real dev value from the table below (or your own environment's). A `$VAR` in a snippet is a shell variable set earlier in that snippet. Every section leads with a one-line **"When you need this."**

### Environment values (dev)

| Placeholder | Dev value | What it is |
|-------------|-----------|------------|
| `<RG>` | `rg-antkart-dev-eastus` | Resource group holding all platform resources |
| `<LOCATION>` | `eastus` | Azure region |
| `<CLUSTER>` | `aks-antkart-dev` | AKS cluster |
| `<ACR>` | `acrantkartdev` | Container registry (login server `acrantkartdev.azurecr.io`) |
| `<NS>` | `antkart` | Kubernetes namespace for the services |
| `<VAULT>` | `kv-antkart-dev` | Key Vault |
| `<COSMOS>` | `cosmos-antkart-dev` | Cosmos DB account (MongoDB API), database `antkart-products` |
| `<PG>` | `psql-antkart-dev-eus2` | PostgreSQL Flexible Server |
| `<REDIS>` | `redis-antkart-dev` | Azure Managed Redis |
| `<SB>` | `sb-antkart-dev` | Service Bus namespace |
| `<EVGT>` | `evgt-antkart-dev` | Event Grid custom topic |
| `<ACS>` | `acs-antkart-dev` | Communication Services (email service `acs-email-antkart-dev`) |
| `<FN>` | `func-antkart-notifications-dev` | Notifications Function App |
| `<VNET>` | `vnet-antkart-dev-eastus` | Virtual network (subnets: `aks`, `private-endpoints`, `gateway`) |
| `<LAW>` | `log-antkart-dev` | Log Analytics workspace (App Insights `appi-antkart-dev`) |
| `<API_APP>` | `antkart-api-dev` | API app registration (App ID URI `api://antkart-api-dev`) |
| `<TENANT>` | `4cacc56a-0d38-46c4-ba20-429d51d7b449` | Entra tenant id |
| `<SERVICE>` | `products` \| `cart` \| `order` \| `payments` \| `discount` \| `gateway` | Short service name |

In-cluster Services and ServiceAccounts are named `ak-<service>` (e.g. `ak-products`); per-service managed identities are `id-ak-<service>-dev`. The Terraform remote state lives in resource group `rg-antkart-tfstate`, storage account `stantkarttfstate`, container `tfstate`.

---

## A. Azure CLI — authentication and context

> **When you need this:** at the start of any session, to sign in and confirm you are pointed at the right subscription and identity.

```bash
az login
```
- Opens a browser to sign in interactively and caches the token. Add `--use-device-code` when no browser is available (headless/SSH); add `--tenant <TENANT>` to sign in to a specific directory.

```bash
az account show -o table
```
- Shows the **currently active** subscription and tenant. `-o table` renders a compact table (default is JSON). Run this first — most `az` commands act on the active subscription.

```bash
az account list -o table
```
- Lists every subscription your identity can see, with an `IsDefault` column marking the active one.

```bash
az account set --subscription "<subscription-id>"
```
- Makes `<subscription-id>` the active subscription for subsequent commands. Use the id (a GUID) or the exact subscription name.

```bash
az ad signed-in-user show --query id -o tsv
```
- Prints **your Entra object id** (the stable principal id used to scope role assignments — not the same as your email or the app's client id). `--query id` selects just the `id` field via JMESPath; `-o tsv` prints the bare value with no quotes, ideal for capturing into a variable: `OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)`.

---

## B. Azure CLI — resource provisioning and inspection

> **When you need this:** to inspect or operate the managed Azure resources directly (outside Terraform), e.g. read a Key Vault secret, start/stop the database, or check what images are in the registry.

Most resources are **provisioned by Terraform/Terragrunt** (see [section G](#g-terraform--terragrunt) and the [Infrastructure Guide](infrastructure-guide.md)). The commands here are for **inspection and day-to-day operations**, not for creating what IaC owns.

### Resource groups

```bash
az group show --name <RG> -o table
az group list --query "[?starts_with(name,'rg-antkart')].{name:name, location:location}" -o table
```
- `show` confirms the group exists and its region. The `list` `--query` filters to AntKart groups and projects two columns (JMESPath: `[?condition].{alias:field}`).

### Container Registry (ACR)

```bash
az acr login --name <ACR>
```
- Authenticates the local Docker client to the registry using **your Entra identity** (the registry has its admin account disabled — no username/password). Required before `docker push`/`pull` to `acrantkartdev.azurecr.io`.

```bash
az acr repository list --name <ACR> -o table
az acr repository show-tags --name <ACR> --repository antkart/<SERVICE> -o table
```
- `repository list` shows every image repository (e.g. `antkart/products`, `antkart/shoppingcart`). `show-tags` lists the tags of one repository — use it to confirm a tag was pushed. Add `--orderby time_desc` to see the newest first.

```bash
az acr build -r <ACR> -t antkart/<SERVICE>:<TAG> -f AK.Products/AK.Products.API/Dockerfile .
```
- Builds the image **server-side in the registry** and pushes it, so no local Docker is needed. `-r` is the registry, `-t` the tag, `-f` the Dockerfile, and the trailing `.` is the build context (**repository root** — required, see [section H](#h-docker)).

### PostgreSQL Flexible Server — state, start/stop, firewall

> **Note — the `eus2` in the server name is deliberate.** The Flexible Server is named `psql-antkart-dev-eus2` and lives in **East US 2**, while the rest of the platform is in **East US** (`<LOCATION>` = `eastus`). This is intentional, not a typo: at provisioning time East US was capacity-restricted for the Flexible Server SKU, so the server was created in East US 2. Cross-region latency to the AKS cluster is negligible for this workload. **Do not "correct" the name to match the region** — the server, its DNS name, and the vaulted connection strings all depend on it.

```bash
az postgres flexible-server show -g <RG> -n <PG> --query "{name:name, state:state, fqdn:fullyQualifiedDomainName}" -o json
```
- Shows the server and its `state` (`Ready` / `Stopped`). The `--query` projects the three fields worth checking.

```bash
az postgres flexible-server stop  -g <RG> -n <PG>
az postgres flexible-server start -g <RG> -n <PG>
```
- Stop the server when idle to save cost; start it before running the relational services. A stopped Flexible Server auto-starts after 7 days if left stopped.

```bash
MY_IP=$(curl -s https://ifconfig.me)        # bash;  PowerShell: (Invoke-RestMethod ifconfig.me/ip)
az postgres flexible-server firewall-rule create \
  -g <RG> -n <PG> \
  --rule-name laptop-$(date +%Y%m%d) \
  --start-ip-address "$MY_IP" --end-ip-address "$MY_IP"
```
- Adds a firewall rule so **your current public IP** can reach the server (needed for `psql`/EF migrations from a laptop). `curl -s https://ifconfig.me` returns your public IP; `--rule-name` is any label; start/end IP being equal opens exactly one address. Remove stale rules with `az postgres flexible-server firewall-rule delete`. In the cluster, services reach Postgres over the VNet — no firewall rule needed.

### Key Vault — secrets

```bash
az keyvault secret list --vault-name <VAULT> --query "[].name" -o tsv
```
- Lists secret **names only** (never values). `[].name` projects the name of each item.

```bash
az keyvault secret show --vault-name <VAULT> --name "ProductsCosmosConnectionString" --query value -o tsv
```
- Prints a secret's **value** — use sparingly and never paste it anywhere. `--name` is the secret name (Key Vault uses `--` where config uses `:`, e.g. `ConnectionStrings--Postgres`).

```bash
az keyvault secret set --vault-name <VAULT> --name "Razorpay--KeyId" --value "<value>"
```
- Creates or updates a secret. This is how secrets enter the platform (services read them at runtime via workload identity); `appsettings` never carries them.

### Communication Services (email)

```bash
az communication show --name <ACS> --resource-group <RG> --query "{name:name, hostName:hostName, provisioningState:provisioningState}" -o json
az communication email domain show --email-service-name acs-email-antkart-dev --name AzureManagedDomain --resource-group <RG> --query "{fromSenderDomain:fromSenderDomain}" -o json
az communication email domain sender-username list --email-service-name acs-email-antkart-dev --domain-name AzureManagedDomain --resource-group <RG> -o table
```
- `communication show` gives the ACS endpoint hostname (what the Function's `Acs:Endpoint` points at). `email domain show` returns the Azure-managed `*.azurecomm.net` sender domain. `sender-username list` shows the MailFrom addresses — the Function's `Acs:SenderAddress` **must** exactly match one of these.

### Event Grid — topic and subscriptions

```bash
az eventgrid topic show --name <EVGT> --resource-group <RG> --query "{endpoint:endpoint, provisioningState:provisioningState}" -o json
az eventgrid event-subscription list --source-resource-id $(az eventgrid topic show -n <EVGT> -g <RG> --query id -o tsv) -o table
```
- `topic show` returns the publish endpoint (Order/Payments publish here). `event-subscription list` shows the subscriptions delivering events to the Function; the nested `$(...)` resolves the topic's resource id to scope the query.

```bash
az eventgrid event-subscription create \
  --name notifications \
  --source-resource-id $(az eventgrid topic show -n <EVGT> -g <RG> --query id -o tsv) \
  --endpoint-type azurefunction \
  --endpoint $(az functionapp function show -g <RG> -n <FN> --function-name OnOrderConfirmed --query id -o tsv)
```
- Creates a subscription routing topic events to a Function. `--source-resource-id` is the topic; `--endpoint-type azurefunction` + `--endpoint` the target function's resource id. (Topology is normally provisioned by IaC; use this only for manual wiring.)

### Function App — inspect and operate

```bash
az functionapp show --resource-group <RG> --name <FN> --query "{state:state, defaultHostName:defaultHostName}" -o json
az functionapp function list --resource-group <RG> --name <FN> -o table
az functionapp config appsettings list --resource-group <RG> --name <FN> -o table
```
- `show` confirms the app is `Running`. `function list` lists the deployed functions (`OnOrderCreated` … `OnPaymentFailed`). `appsettings list` shows the app settings (non-secret config such as `Acs:Endpoint`, `KeyVault:Uri`).

```bash
az functionapp config appsettings set --resource-group <RG> --name <FN> --settings "Acs__SenderAddress=DoNotReply@<subdomain>.azurecomm.net"
az functionapp restart --resource-group <RG> --name <FN>
func azure functionapp publish <FN>
```
- `appsettings set` writes one or more `KEY=VALUE` settings (`__` maps to the config `:` separator). `restart` bounces the app to pick up settings. `func azure functionapp publish` deploys the built Function project (requires **Azure Functions Core Tools**); run it from the `AK.Notification.Functions` folder.

### Managed identity and role assignments

```bash
az identity show -g <RG> -n id-ak-<SERVICE>-dev --query "{clientId:clientId, principalId:principalId}" -o json
```
- Returns a per-service workload identity's **clientId** (goes on the ServiceAccount `azure.workload.identity/client-id` annotation) and **principalId** (used as the assignee when granting roles).

```bash
PRINCIPAL_ID=$(az identity show -g <RG> -n id-ak-products-dev --query principalId -o tsv)
SCOPE=$(az keyvault show -g <RG> -n <VAULT> --query id -o tsv)
az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role "Key Vault Secrets User" --scope "$SCOPE"
```
- Grants a role to a principal at a scope. `--assignee-object-id` is the principal id; `--assignee-principal-type ServicePrincipal` skips a Graph lookup (avoids a transient "principal not found" for a freshly created identity); `--role` is the built-in role name; `--scope` is the target resource id (obtained with `... show --query id -o tsv`). Role grants are normally provisioned by the `role-assignments` / `workload-identity` IaC units.

```bash
az role assignment list --assignee "$PRINCIPAL_ID" --all -o table
```
- Lists every role assignment for a principal across the subscription (`--all`), so you can confirm least-privilege grants.

### VM size availability and vCPU quota

```bash
az vm list-skus --location <LOCATION> --size Standard_D --all --query "[].{name:name, restrictions:restrictions[].reasonCode}" -o table
```
- Lists D-series sizes in the region and any `restrictions` (an empty restrictions list means the size is usable on this subscription). Watch **architecture**: `b*ps_v2` sizes are ARM64 and will not run amd64 images (see [section J](#j-gotchas-and-powershell-notes)).

```bash
az vm list-usage --location <LOCATION> --query "[?contains(localName,'Standard DSv3')].{name:localName, used:currentValue, limit:limit}" -o table
```
- Shows vCPU **quota** usage per family. Confirm the Dsv3 family has headroom before scaling the node pool (`2 × Standard_D2s_v3` = 4 vCPUs).

### AKS available versions

```bash
az aks get-versions --location <LOCATION> -o table
```
- Lists Kubernetes versions offered in the region with their support tier. Pick one marked **`KubernetesOfficial`**; versions offered only under `AKSLongTermSupport` need an LTS-tier subscription (see [section J](#j-gotchas-and-powershell-notes)).

---

## C. Azure CLI — AKS lifecycle and access

> **When you need this:** to start/stop the cluster for cost control, and to get `kubectl` working against this Entra-enabled cluster.

```bash
az aks show -g <RG> -n <CLUSTER> --query "{powerState:powerState.code, k8sVersion:currentKubernetesVersion, fqdn:fqdn}" -o json
az aks stop  -g <RG> -n <CLUSTER>     # deallocates the nodes — stops node billing
az aks start -g <RG> -n <CLUSTER>     # brings the nodes back
```
- `show` reports whether the cluster is `Running` or `Stopped` and its version. `stop`/`start` is the routine for idle periods (see [section I](#i-session-bring-up-and-shutdown)); the ingress load balancer's public IP persists across a stop, so the hostname stays valid.

```bash
az aks get-credentials -g <RG> -n <CLUSTER>
az aks install-cli
kubelogin convert-kubeconfig -l azurecli
kubectl get nodes        # first call triggers an Entra sign-in
```
- `get-credentials` merges the cluster's kubeconfig into `~/.kube/config`. `install-cli` installs `kubectl` **and** `kubelogin`. `kubelogin convert-kubeconfig -l azurecli` rewrites the kubeconfig to obtain tokens via your `az` login (`-l azurecli` = login mode).
- **Why `kubelogin` is required:** the cluster uses **Entra (Azure AD) authentication with Azure RBAC**, so `kubectl` must present an Entra token — the raw kubeconfig alone cannot, and every call would fail to authenticate. In addition, being able to reach the API server is not enough: you also need a **Kubernetes RBAC role assignment on the cluster** (e.g. *Azure Kubernetes Service RBAC Cluster Admin* scoped to the cluster) or every call is `Forbidden`. See the [AKS Guide → Operator Access](aks-guide.md#operator-access) for the role-grant command. Note `install-cli` changes `PATH` — open a **new shell** afterwards.

---

## D. kubectl — everyday operations

> **When you need this:** the day-to-day inspection and control of workloads in the cluster. All service objects live in namespace `<NS>` (`antkart`); `-n <NS>` targets it (or set it as default: `kubectl config set-context --current --namespace=<NS>`).

### Context and cluster

```bash
kubectl config get-contexts            # list contexts; * marks the active one
kubectl config use-context <CLUSTER>   # switch active cluster
kubectl cluster-info                   # API server + CoreDNS endpoints (confirms connectivity)
kubectl get nodes -o wide              # nodes, status, internal IP, OS/kernel/runtime
```

### Inspect resources

```bash
kubectl -n <NS> get pods -o wide
kubectl -n <NS> get deploy,svc,ingress,configmap,secret,serviceaccount
kubectl -n <NS> get events --sort-by=.lastTimestamp
```
- `get` lists objects; `-o wide` adds node/IP columns. Combine kinds with commas. `get events --sort-by=.lastTimestamp` is the single most useful triage command — it shows the recent cluster activity (scheduling, pulls, probe failures) newest-last.

```bash
kubectl -n <NS> describe pod <POD>
kubectl -n <NS> describe deploy ak-<SERVICE>
kubectl -n <NS> describe ingress ak-gateway
```
- `describe` prints the full object **plus its Events** — the first thing to read for a failing pod, deployment rollout, or ingress. `<POD>` comes from `get pods` (e.g. `ak-products-7d9c...`).

### Logs

```bash
kubectl -n <NS> logs deploy/ak-<SERVICE>                # current logs of the deployment's pod
kubectl -n <NS> logs <POD> -f --tail=100                # follow (stream), last 100 lines
kubectl -n <NS> logs <POD> --previous                   # logs of the PREVIOUS (crashed) container
kubectl -n <NS> logs -l app.kubernetes.io/name=ak-<SERVICE> --tail=50   # by label, across pods
kubectl -n <NS> logs <POD> --since=10m                  # last 10 minutes
```
- `-f` streams; `--tail=N` limits to the last N lines; `--previous` is essential for a CrashLoopBackOff (it shows why the last container **died**, which the current logs won't); `-l` selects by label (the chart labels pods `app.kubernetes.io/name=ak-<service>`); `--since` bounds by time.

### Exec, port-forward

```bash
kubectl -n <NS> exec -it <POD> -- <command>
kubectl -n <NS> port-forward deploy/ak-<SERVICE> 8080:8080
```
- `exec -it` runs a command in a container (`-i` interactive, `-t` TTY); everything after `--` is the command. **Note:** the runtime images ship **no shell**, so `exec ... -- sh`/`curl` will fail — use the debug-pod pattern below. `port-forward` maps a local port to the pod's port (`local:pod`), so you can `curl http://localhost:8080/health/ready` against an internal ClusterIP service without ingress.

### Rollouts and scaling

```bash
kubectl -n <NS> rollout restart deploy/ak-<SERVICE>     # recreate pods (picks up a new image on a mutable tag, or a ConfigMap change)
kubectl -n <NS> rollout status  deploy/ak-<SERVICE>     # wait until the rollout completes
kubectl -n <NS> rollout undo    deploy/ak-<SERVICE>     # roll back to the previous ReplicaSet
kubectl -n <NS> scale deploy/ak-<SERVICE> --replicas=2  # change replica count
```
- `rollout restart` is how you force a fresh image pull when the tag is mutable (see [section J](#j-gotchas-and-powershell-notes)) or reload a mounted ConfigMap (e.g. the gateway's `ocelot.json`). `rollout status` blocks until ready or failed. `rollout undo` reverts the last rollout. `scale` sets replica count.

### Apply and delete

```bash
kubectl apply -f deploy/cert-manager/cluster-issuer-staging.yaml
kubectl apply --dry-run=client -f <file>.yaml           # validate/parse without contacting the server for changes
kubectl -n <NS> delete pod <POD>                        # delete a pod (the Deployment recreates it)
```
- `apply -f` creates or updates from a manifest. `--dry-run=client` parses and validates locally. `delete pod` is the safe way to force one pod to be recreated (the Deployment/ReplicaSet immediately replaces it).

### Ephemeral debug pod (no shell in runtime images)

```bash
kubectl -n <NS> run debug --rm -it --image=nicolaka/netshoot --restart=Never -- bash
kubectl -n <NS> debug <POD> -it --image=nicolaka/netshoot --target=<container>
```
- The service images contain **only the .NET runtime — no shell, curl, dig, or nslookup** — so you cannot `exec` tools into them. Instead: `kubectl run debug --rm -it` starts a **throwaway** pod (`--rm` deletes it on exit, `--restart=Never` makes it a one-shot pod) from a tools image (`nicolaka/netshoot` carries curl/dig/nc/etc.) — use it to test in-cluster DNS and reach services (e.g. `curl http://ak-products:8080/health/ready`). `kubectl debug` attaches an **ephemeral container** that shares the target pod's network/PID namespace, to inspect a running pod without a shell of its own.

---

## E. kubectl — troubleshooting workflows

> **When you need this:** something is wrong in the cluster and you want the exact commands in order. Each path reads top-down. Deeper explanations: [AKS Guide → Troubleshooting](aks-guide.md#troubleshooting) and [→ Ingress and TLS](aks-guide.md#ingress-and-tls).

### A pod is `CrashLoopBackOff`

```bash
kubectl -n <NS> get pods
kubectl -n <NS> logs <POD> --previous           # WHY the last container died — read this first
kubectl -n <NS> describe pod <POD>              # Events + last state + exit code
```
- The container starts then exits repeatedly. `logs --previous` shows the exception/stack that killed it (e.g. a missing Key Vault secret, a bad connection string). `describe` shows the exit code and restart count. Common cause here: a config/secret the service needs at boot is missing or wrong (see [Container Configuration](container-configuration.md)).

### A pod is `Pending`

```bash
kubectl -n <NS> describe pod <POD>              # read the Events at the bottom
kubectl get nodes -o wide
kubectl top nodes                               # requires the metrics server
```
- Pending = not scheduled. `describe` Events state why: `Insufficient cpu/memory` (node full — check requests vs [node capacity](aks-guide.md#the-aks-cluster)), or an unschedulable taint/affinity. `get nodes`/`top nodes` show whether the two-node pool has room.

### An image will not pull (`ImagePullBackOff` / `ErrImagePull`)

```bash
kubectl -n <NS> describe pod <POD>              # Events show the exact registry error
az acr repository show-tags --name <ACR> --repository antkart/<SERVICE> -o table
```
- `describe` Events give the reason: `not found` (wrong repository/tag — verify with `show-tags`; note cart pulls `antkart/shoppingcart`, not `antkart/cart`), `unauthorized` (the kubelet identity is missing **AcrPull** on the registry — see [AKS Guide](aks-guide.md#the-aks-cluster)), or `manifest unknown` (tag never pushed). A stale image on a reused mutable tag is different — see [section J](#j-gotchas-and-powershell-notes).

### A probe is failing (pod restarts, or never becomes Ready)

```bash
kubectl -n <NS> describe pod <POD>              # Events: "Liveness/Readiness/Startup probe failed"
kubectl -n <NS> logs <POD>                      # did the app finish starting?
kubectl -n <NS> port-forward <POD> 8080:8080
curl.exe -i http://localhost:8080/health/ready  # hit the probe endpoint directly
```
- The `startupProbe` gates liveness/readiness so a slow Key-Vault-at-boot start doesn't trigger a restart. If liveness fails after startup, the process is unresponsive; if readiness stays failing, a dependency the readiness check tolerates is degraded. Port-forward and curl `/health/live` (shallow) and `/health/ready` (tolerant) to see the actual response. (AK.Discount uses **TCP** probes — it serves h2c gRPC, so an HTTP probe wouldn't apply.)

### A service is unreachable (from another pod)

```bash
kubectl -n <NS> get svc ak-<SERVICE> -o wide           # ClusterIP + ports
kubectl -n <NS> get endpoints ak-<SERVICE>             # are any pod IPs backing it?
kubectl -n <NS> run debug --rm -it --image=nicolaka/netshoot --restart=Never -- \
  curl -s -o /dev/null -w "%{http_code}\n" http://ak-<SERVICE>:8080/health/ready
```
- `get svc` confirms the ClusterIP and port (8080). `get endpoints` is key: **empty endpoints** means no Ready pod matches the selector (fix the pod/readiness first). The debug pod tests the actual in-cluster call over Service DNS.

### A certificate stays `Pending`

Walk the cert-manager chain **Certificate → CertificateRequest → Order → Challenge**:

```bash
kubectl -n <NS> get certificate,certificaterequest,order,challenge
kubectl -n <NS> describe certificate ak-gateway-tls
kubectl -n <NS> describe challenge <name>          # HTTP-01 validation detail
nslookup api.antkart.in                            # must resolve to the ingress controller public IP
kubectl -n ingress-nginx get svc ingress-nginx-controller   # EXTERNAL-IP must not be <pending>
```
- Each object references the next; `describe` the one that's stuck. Common causes: the host (`api.antkart.in`, or a `<public-ip>.nip.io` fallback) doesn't resolve to the controller IP; the controller has no public IP yet; the HTTP-01 `/.well-known/acme-challenge` path can't be reached (**custom NSG** not allowing inbound 80 — see [section J](#j-gotchas-and-powershell-notes)); an invalid issuer email; or a production rate-limit lockout. Full detail: [AKS Guide → Ingress troubleshooting](aks-guide.md#ingress-and-tls).

### DNS resolution inside the cluster

```bash
kubectl -n <NS> run debug --rm -it --image=nicolaka/netshoot --restart=Never -- \
  nslookup ak-products.antkart.svc.cluster.local
kubectl -n kube-system get pods -l k8s-app=kube-dns    # CoreDNS pods healthy?
```
- In-cluster names resolve as `<service>.<namespace>.svc.cluster.local`. From a debug pod, `nslookup` a Service name to confirm CoreDNS resolves it; if it fails for everything, check the CoreDNS pods in `kube-system`.

---

## F. Helm

> **When you need this:** to install, upgrade, inspect, or roll back the six services and the platform add-ons. The repo runs **Helm 4**, which uses **Server-Side Apply by default** (the API server computes the diff and tracks field ownership). A **release** is a named, versioned installation of a chart into a namespace — each service is its own release (`ak-<service>`), all from the one generic chart `deploy/helm/antkart-service`.

```bash
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo add jetstack https://charts.jetstack.io
helm repo update
```
- `repo add` registers a chart repository under a name; `repo update` refreshes the local index of available chart versions. Needed for the ingress-nginx and cert-manager add-ons.

```bash
helm upgrade --install ak-<SERVICE> deploy/helm/antkart-service -n <NS> -f deploy/helm/values/<SERVICE>.yaml
```
- `upgrade --install` installs the release if absent, upgrades it if present (idempotent). `ak-<SERVICE>` is the release name; `deploy/helm/antkart-service` is the chart; `-n <NS>` the namespace; `-f` supplies the per-service values file. Add `--create-namespace` on first install.

```bash
helm upgrade ak-gateway deploy/helm/antkart-service -n <NS> -f deploy/helm/values/gateway.yaml \
  --set ingress.enabled=true --set ingress.host=api.antkart.in --set image.tag=<TAG>
```
- `-f` loads a values **file**; `--set key=value` overrides a single value on the command line (takes precedence over `-f`). Used here to enable the gateway ingress with the external host `api.antkart.in`, or pin an image tag. **On the delivered platform the host/issuer/tag live in Git** and Argo CD reconciles them (auto-sync + self-heal) — a `--set` would be reverted; this manual form is the bootstrap / non-GitOps path. Where no domain exists, substitute `<PUBLIC_IP>.nip.io`.

```bash
helm list -n <NS>                       # releases in the namespace, their revision and chart version
helm status ak-<SERVICE> -n <NS>        # current release state + notes
helm history ak-<SERVICE> -n <NS>       # revision history (each upgrade bumps the revision)
helm rollback ak-<SERVICE> <REVISION> -n <NS>   # revert to a previous revision
helm uninstall ak-<SERVICE> -n <NS>     # remove the release and its objects
```
- `list` shows what's installed. `history` lists revisions; `rollback <REVISION>` restores one (find the number from `history`). `uninstall` deletes the release.

```bash
helm lint deploy/helm/antkart-service -f deploy/helm/values/<SERVICE>.yaml
helm template ak-<SERVICE> deploy/helm/antkart-service -n <NS> -f deploy/helm/values/<SERVICE>.yaml
```
- `lint` checks the chart + values for errors **without a cluster**. `template` renders the manifests locally to stdout (no install) — use it to verify what will be applied (e.g. the ServiceAccount `client-id` annotation, the rendered ocelot ConfigMap). Neither contacts the cluster.

---

## G. Terraform / Terragrunt

> **When you need this:** to provision, change, or tear down the Azure infrastructure. Run every command **from a unit folder** under `infrastructure/environments/dev/<unit>`. Terragrunt is a thin wrapper over Terraform (`terragrunt <cmd>` = `terraform <cmd>` + injected backend/provider). Concepts: [IaC fundamentals](iac-concepts.md); per-resource walkthrough: [Infrastructure Guide](infrastructure-guide.md).

```bash
cd infrastructure/environments/dev/<unit>
terragrunt init                 # wire the remote-state backend, download providers, resolve dependencies
terragrunt validate             # check the config is syntactically and internally valid
terragrunt plan                 # read-only preview of what would change
terragrunt apply                # make it real (prompts for confirmation)
```
- `init` generates `backend.tf`/`provider.tf` from the shared root and connects to the Azure-AD-authenticated remote state. `validate` is offline. `plan` shows adds/changes/destroys — **run it before every apply**. `apply` executes after you type `yes` (add `-auto-approve` only in automation).

```bash
terragrunt init -upgrade
```
- Re-resolves provider versions and **updates this unit's `.terraform.lock.hcl`**. Each unit pins providers in its **own** lock file, so after changing the shared provider constraints you must run `init -upgrade` in **each affected unit** and commit the refreshed lock files (a common gotcha).

```bash
terragrunt output                       # show this unit's outputs
terragrunt output -raw <name>           # one output, unquoted (for scripting)
terragrunt destroy                      # tear down this unit's resources (prompts)
```
- `output` prints values other units consume (e.g. the AKS `oidc_issuer_url`, the workload-identity `client_id`s). `destroy` removes what the unit created — note the `resource-group` unit has `prevent_destroy = true` as a safety guard.

**Dependency resolution & apply order.** Units declare `dependency` blocks; Terragrunt applies upstream units first and passes their outputs down. From `infrastructure/environments/dev`, `terragrunt run-all apply` builds everything respecting that order:

```
resource-group
  → networking, container-registry, key-vault, observability, cosmosdb, postgresql, redis, servicebus, eventgrid, communication-services
  → function-app (needs observability)
  → role-assignments (needs function-app, key-vault, servicebus, eventgrid)
  → aks (needs networking, container-registry, observability)
  → workload-identity (needs aks, key-vault, servicebus, eventgrid)
app-registration and governance are independent of the above.
```
See [infrastructure/README](../../infrastructure/README.md) for the authoritative map. Two bootstrap steps run with `az` **before any Terraform** — the remote-state storage account/container and the Terraform service principal. These are documented in [section K](#k-one-time-bootstrap--terraform-backend--service-principal); they run **once**, before the first `terragrunt init`.

---

## H. Docker

> **When you need this:** to build and push a service image locally (the alternative to `az acr build`). See also [AKS Guide → Build and Push](aks-guide.md#build-and-push-to-the-azure-container-registry).

```bash
az acr login --name <ACR>       # authenticate first (section B)
docker build -f AK.Products/AK.Products.API/Dockerfile -t acrantkartdev.azurecr.io/antkart/products:<TAG> .
docker push acrantkartdev.azurecr.io/antkart/products:<TAG>
```
- `-f` selects the service's Dockerfile; the trailing **`.` is the build context = repository root**. The context **must** be the repo root because each service references the shared `AK.BuildingBlocks` project and `nuget.config`, which live **above** the service folder — building from the service folder would fail to find them. `-t` tags the image with the full ACR path so `push` knows where to send it. Image repositories are `antkart/<service>` (cart's is `antkart/shoppingcart`).

```bash
docker images acrantkartdev.azurecr.io/antkart/*      # local images for the platform
docker tag <IMAGE> acrantkartdev.azurecr.io/antkart/products:<NEWTAG>   # add another tag
docker system df                                      # disk used by images/containers/cache
docker builder prune -f                               # reclaim build-cache space
docker image prune -a -f                              # remove dangling/unused images
```
- `.dockerignore` at the repo root keeps the (root) build context small and, critically, keeps `appsettings.Development.json`, local secrets, `bin/`, `obj/`, and `.git/` **out of image layers** — image layers are inspectable and pushed to a registry. `system df` shows what's consuming disk; `prune` reclaims it (`-a` also removes unused, not just dangling, images — use with care).

---

## I. Session bring-up and shutdown

> **When you need this:** to bring the platform up at the start of a session, and shut it down afterwards to control cost. The AKS node pool and the PostgreSQL server are the two billable compute resources that are stopped when idle; everything else is always-on managed services (Cosmos DB, Service Bus, Event Grid, Key Vault, ACS) with low/no idle cost.

### Start-of-session bring-up (in order)

```bash
az login && az account set --subscription "<subscription-id>"   # 1. auth + context (section A)
az aks start -g <RG> -n <CLUSTER>                                # 2. start the cluster nodes
az aks show -g <RG> -n <CLUSTER> --query powerState.code -o tsv  # 3. verify -> expect "Running"
az postgres flexible-server start -g <RG> -n <PG>               # 4. start the relational DB (Order/Payments/Discount need it)
az aks get-credentials -g <RG> -n <CLUSTER>                     # 5. kubeconfig (if not cached)
kubelogin convert-kubeconfig -l azurecli                        # 6. Entra login for kubectl
kubectl -n <NS> get pods                                        # 7. confirm the fleet is Running
kubectl -n <NS> get ingress ak-gateway                          # 8. confirm the ingress host (api.antkart.in)
curl.exe https://api.antkart.in/health/live                     # 9. end-to-end reachability -> 200 "Healthy" (trusted prod cert, no -k)
```
- **Order matters:** the cluster and database must be running before the pods can become Ready. `az aks show --query powerState.code -o tsv` confirms the start finished (`Running`) before you spend time on `kubectl`. If PostgreSQL is still stopped when the pods start, **Order/Payments/Discount will `CrashLoopBackOff` and then self-heal automatically** once the DB is up — the Kubernetes control loop keeps restarting them until the dependency returns, so no pod intervention is needed (starting the DB in step 4 avoids the churn).
- **Argo CD resumes on restart.** The `argocd` namespace and its Applications persist across a stop; when the nodes come back, Argo CD reconciles the cluster to Git automatically — no re-install or re-apply. Deploying while stopped is just a `git push` that reconciles once the cluster is running again.

### End-of-session shutdown (cost control)

```bash
az aks stop  -g <RG> -n <CLUSTER>                                # stop node billing (control plane Free tier costs nothing)
az postgres flexible-server stop -g <RG> -n <PG>                # stop the DB compute
az aks show -g <RG> -n <CLUSTER> --query powerState.code -o tsv  # verify -> expect "Stopped"
```
- **Stops (billing paused, state kept):** the AKS node pool and the PostgreSQL server. `powerState.code` should read `Stopped` once `az aks stop` returns.
- **Persists automatically (no action, low/no idle cost):** Cosmos DB (serverless), Service Bus, Event Grid, Key Vault, ACS, the container registry, Log Analytics — and the **ingress load balancer's public IP**, which is **retained across an `az aks stop`, so the `api.antkart.in` hostname and the issued TLS certificate stay valid** when you start again (verified after a stop/redeploy: the existing production certificate was served on the same host with no re-issue).
- **Only deleting removes cost/data:** to fully decommission, `terragrunt destroy` per unit (or `run-all destroy`). Deleting the cluster releases the LB public IP — the GoDaddy **A record for `api.antkart.in` would then need repointing** at the new IP. Redis and Cosmos hold data; destroying them is irreversible.

---

## J. Gotchas and PowerShell notes

> **When you need this:** to avoid the traps that cost time on this platform.

### PowerShell specifics

- **`curl` is an alias for `Invoke-WebRequest`, not the real curl.** So flags like `-k` (insecure TLS), `-H` (header), `-w`, `-s`, `-o` fail or mean something else. Use **`curl.exe`** explicitly whenever you need those (e.g. `curl.exe -k https://<host>/health/live`, `curl.exe -H "Authorization: Bearer $TOKEN" ...`).
- **`PATH` changes need a new shell.** After `az aks install-cli` (or installing any CLI), open a **new** terminal so `kubectl`/`kubelogin` resolve.
- **Quote `--query` (JMESPath) expressions.** PowerShell parses `[`, `]`, `?`, and `{` — always wrap the query in quotes: `az ... --query "[?state=='Ready'].name" -o tsv`.
- **Line continuation is a backtick (`` ` ``), not a backslash.** In bash, multi-line commands continue with `\`; in PowerShell use `` ` `` at end of line. (This document's bash snippets use `\`.)
- **`-o tsv` for capturing values.** JSON/table output is for humans; `--query <field> -o tsv` gives a bare value to assign to a variable in either shell.

### Platform gotchas

- **AKS version support tiers.** A version offered only under `AKSLongTermSupport` requires an LTS-tier subscription and is otherwise rejected. Choose one marked `KubernetesOfficial` (`az aks get-versions`).
- **Subscription VM SKU allow-lists, and ARM vs amd64.** The intended VM size may be restricted in a region/subscription (`az vm list-skus ... restrictions`). Watch **architecture**: the available `b*ps_v2` sizes are **ARM64** and will **not** run the amd64-built service images — check architecture, not just availability.
- **Mutable image tags with `imagePullPolicy: IfNotPresent` serve a stale image.** A node that cached a tag keeps the old image after you push a new one to the **same** tag. Fix: use an **immutable tag** (the commit SHA); as a stop-gap `kubectl rollout restart` / delete the pod to force a pull. Tracked as [KI-004](../KNOWN_ISSUES.md).
- **Custom NSGs don't get automatic LoadBalancer rules.** With a bring-your-own VNet and a customer-managed NSG, AKS does **not** add the inbound rules a public Service needs, so internet traffic (and the ACME HTTP-01 challenge) is dropped by the deny-all baseline — the `AzureLoadBalancer` tag covers only health probes. Inbound 80/443 from the `Internet` tag must be added on the ingress subnet's NSG (the networking module's `allow_internet_ingress` flag). See [AKS Guide → Ingress troubleshooting](aks-guide.md#ingress-and-tls).

---

## K. One-time bootstrap — Terraform backend & service principal

> **When you need this:** exactly **once**, when standing up the environment from nothing — **before the first `terragrunt init`**. Terraform needs somewhere to keep its state and an identity to authenticate as; neither can be created by Terraform itself (chicken-and-egg), so both are created here with `az`. After this runs once, everything else is Terraform/Terragrunt ([section G](#g-terraform--terragrunt)). Concept + rationale: [Infrastructure Guide](infrastructure-guide.md) and [ADR-012](../adr/ADR-012-iac-with-terraform-terragrunt.md).

> **Secrets:** every value shown as `<...>` below is a placeholder — **no real subscription id, tenant id, client id, or client secret is ever committed**. The generated service-principal password is stored **only** in the CI/CD secret store (GitHub Actions repository secrets / environment secrets) and, for local runs, in the operator's own environment — never in this repo, `appsettings`, or Terraform files. The workloads themselves use **workload identity / OIDC federation** and hold no secret at all (see [section G](#g-terraform--terragrunt) and [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md)); this service principal exists for the bootstrap/CI apply, not for the running services.

### K.1 — Terraform remote-state backend (storage account + container)

```bash
az group create --name rg-antkart-tfstate --location <LOCATION>
```
- Creates the resource group that holds **only** the Terraform state — kept separate from the platform resource group so a `terragrunt destroy` of the platform can never delete the state. `--location` is the Azure region (dev: `eastus`).

```bash
az storage account create \
  --name stantkarttfstate \
  --resource-group rg-antkart-tfstate \
  --location <LOCATION> \
  --sku Standard_LRS \
  --encryption-services blob \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false
```
- Creates the storage account backing the state. `--sku Standard_LRS` = locally-redundant (sufficient for state); `--encryption-services blob` encrypts blob data at rest; `--min-tls-version TLS1_2` enforces modern TLS; `--allow-blob-public-access false` guarantees the state container can never be exposed publicly. The account name must be globally unique and lowercase (`stantkarttfstate`).

```bash
az storage container create \
  --name tfstate \
  --account-name stantkarttfstate \
  --auth-mode login
```
- Creates the blob **container** (`tfstate`) that holds each unit's `terraform.tfstate`. `--auth-mode login` uses **your Entra identity** (not an account key) to create it — no storage key is fetched or stored. This matches the backend config Terragrunt injects (`resource_group_name = rg-antkart-tfstate`, `storage_account_name = stantkarttfstate`, `container_name = tfstate`), which is why [section B](#b-azure-cli--resource-provisioning-and-inspection) lists these exact names.

### K.2 — Terraform automation service principal

```bash
az ad sp create-for-rbac \
  --name "sp-antkart-terraform-dev" \
  --role "Contributor" \
  --scopes "/subscriptions/<subscription-id>"
```
- Creates an Entra **service principal** for Terraform to authenticate as when provisioning. `--name` is the app display name; `--role "Contributor"` lets it create/modify resources in the subscription; `--scopes` bounds that role to the one subscription (never broader). The command prints `appId`, `password`, and `tenant` **once** — these are the only time the password is shown:

  ```jsonc
  {
    "appId":    "<client-id>",       // → ARM_CLIENT_ID
    "password": "<client-secret>",   // → ARM_CLIENT_SECRET  (shown once; store immediately, never commit)
    "tenant":   "<tenant-id>"        // → ARM_TENANT_ID
  }
  ```

> **Contributor is not enough on its own for role-assignment units.** The `role-assignments` / `workload-identity` units create RBAC role assignments, which requires **User Access Administrator** (or a custom role with `Microsoft.Authorization/roleAssignments/write`). Grant it additionally, scoped to the subscription:
>
> ```bash
> az role assignment create \
>   --assignee "<client-id>" \
>   --role "User Access Administrator" \
>   --scope "/subscriptions/<subscription-id>"
> ```
> - `--assignee` is the service principal's `appId`/client id from above.

### K.3 — Supplying the credentials to Terraform

The AzureRM provider reads these from environment variables — set them in the shell (local) or as CI secrets (GitHub Actions); **do not** put them in `.tf`/`.tfvars` files:

```bash
export ARM_CLIENT_ID="<client-id>"
export ARM_CLIENT_SECRET="<client-secret>"      # from K.2 — the value shown once
export ARM_TENANT_ID="<tenant-id>"
export ARM_SUBSCRIPTION_ID="<subscription-id>"
# PowerShell equivalent: $env:ARM_CLIENT_ID = "<client-id>"  (etc.)
```
- `ARM_CLIENT_ID` / `ARM_CLIENT_SECRET` / `ARM_TENANT_ID` authenticate the service principal; `ARM_SUBSCRIPTION_ID` selects the target subscription. In CI these are repository/environment **secrets**; the running services never use them. With these set, `terragrunt init` in any unit connects to the `stantkarttfstate`/`tfstate` backend and `terragrunt apply` provisions as the service principal.

---

## L. External provider verification — Razorpay (payments)

> **When you need this:** during payment testing, to confirm the **Razorpay sandbox** credentials the platform holds are valid and to inspect a specific test payment/order from the provider side. This verifies an **external provider**, not an AntKart service — the AntKart payment flow itself is exercised in [Full-cloud end-to-end](../test/1-full-cloud-end-to-end.md). Sandbox test cards/OTP: see [AK.Payments design doc](../../AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md).

> **Secrets:** the Razorpay key id/secret are **sandbox** credentials stored in Key Vault as `Razorpay--KeyId` / `Razorpay--KeySecret` (see [section B → Key Vault](#b-azure-cli--resource-provisioning-and-inspection)) — never committed. Show them below only as placeholders. Read them at test time from the vault; do not paste real values into a shell history file.

```bash
# Pull the sandbox credentials from Key Vault into shell vars (they never leave your session)
RZP_KEY_ID=$(az keyvault secret show --vault-name <VAULT> --name "Razorpay--KeyId"     --query value -o tsv)
RZP_KEY_SECRET=$(az keyvault secret show --vault-name <VAULT> --name "Razorpay--KeySecret" --query value -o tsv)
```
- Resolves the vaulted secrets to `<razorpay-key-id>` / `<razorpay-key-secret>` **in memory only**. `--query value -o tsv` prints the bare value. Never echo these.

```bash
# 1. Verify the credentials are accepted by the Razorpay API (lists recent payments)
curl.exe -s -u "$RZP_KEY_ID:$RZP_KEY_SECRET" "https://api.razorpay.com/v1/payments?count=5"
```
- Confirms the key pair authenticates. `-u "id:secret"` sends HTTP Basic auth (Razorpay's scheme); a `200` with a `items` array proves the credentials are valid, a `401` means they are wrong or expired. **`curl.exe`** (not the PowerShell `curl` alias) is required for `-u`/`-s` — see [section J](#j-gotchas-and-powershell-notes).

```bash
# 2. Inspect one payment the platform created during a test (id from the Payments record / logs)
curl.exe -s -u "$RZP_KEY_ID:$RZP_KEY_SECRET" "https://api.razorpay.com/v1/payments/<razorpay-payment-id>"
```
- Fetches a single payment by its Razorpay id (`pay_...`, captured by AK.Payments at initiate/verify time). Use it to confirm status (`created` / `authorized` / `captured` / `failed`) matches what AntKart recorded — the two must agree after `VerifyPayment` runs.

```bash
# 3. Inspect the Razorpay order behind a payment (order id = rzp order, not the AntKart order number)
curl.exe -s -u "$RZP_KEY_ID:$RZP_KEY_SECRET" "https://api.razorpay.com/v1/orders/<razorpay-order-id>"
```
- Fetches the Razorpay **order** (`order_...`) that AK.Payments created before checkout. Note this is Razorpay's order id, **distinct** from AntKart's `ORD-yyyyMMdd-XXXXXXXX` order number. Use it to cross-check the amount/currency and receipt.

Clear the variables when done: `unset RZP_KEY_ID RZP_KEY_SECRET` (bash) / `Remove-Item Env:RZP_KEY_ID,Env:RZP_KEY_SECRET` (PowerShell).

---

## M. Argo CD (GitOps)

> **When you need this:** to install, operate, and observe Argo CD, which drives the cluster's desired state from Git. Concept and full runbook: [GitOps Guide](gitops-guide.md); manifests: [deploy/argocd/README](../../deploy/argocd/README.md).

### Install and access

```bash
kubectl create namespace argocd
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
kubectl -n argocd rollout status deploy/argocd-server --timeout=180s
```
- `create namespace argocd` — Argo CD runs in, and reconciles Application/ApplicationSet objects only from, its own namespace. `apply -f <install.yaml>` — the official non-HA install (`stable` = latest release; swap for `.../argo-cd/<version>/manifests/install.yaml` to pin). `rollout status --timeout=180s` blocks until the API server is available.

```bash
# Initial admin password (username: admin) — bash
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d; echo
```
- Reads the auto-generated bootstrap password; `-o jsonpath` selects the base64 field, `base64 -d` decodes it. Change it after first login (`argocd account update-password`) and delete the secret. PowerShell decode: `[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}")))`.

```bash
kubectl -n argocd port-forward svc/argocd-server 8080:443
argocd login localhost:8080 --username admin --password '<password>' --insecure
```
- `port-forward 8080:443` maps local `8080` to the server's `443`; browse **https://localhost:8080**. `argocd login ... --insecure` accepts the self-signed cert on the port-forward so the `argocd` CLI can be used.

### Apply the project and Applications (order matters)

```bash
kubectl apply -f deploy/argocd/appproject-antkart.yaml          # 1. the AppProject FIRST
kubectl apply -f deploy/argocd/applicationset-antkart.yaml      # 2. then the Applications (ApplicationSet)
#   OR: kubectl apply -f deploy/argocd/applications/            #    the six standalone Applications
```
- **The AppProject must exist before any Application.** Each Application declares `project: antkart`; applying one first is rejected with `application is not allowed in project antkart`. Apply the ApplicationSet **or** the standalone Applications, never both (identical names collide).

### List, diff, sync

```bash
argocd app list                                 # all Applications: SYNC + HEALTH
argocd app get ak-products                       # one Application: per-resource sync/health + parameters
argocd app diff ak-products                      # what a sync WOULD change (live vs Git) — read before syncing
argocd app sync ak-products                      # apply Git state now (one-shot reconcile)
argocd app wait ak-products --health             # block until Healthy
kubectl -n argocd get applications               # the same, straight from the CRD (no argocd CLI needed)
```
- `app diff` is the pre-sync safety check (for an already-correct service the only diff is Argo's tracking-id annotation). `app sync` is the CLI equivalent of clicking **Sync**. `app wait --health` gates the next step on readiness.

### Enable automated sync / self-heal / prune

```bash
argocd app set ak-products --sync-policy automated   # auto-sync on every Git change
argocd app set ak-products --self-heal               # also revert live drift back to Git
argocd app set ak-products --auto-prune              # also delete resources removed from Git
argocd app set ak-products --sync-policy none         # back to manual
```
- `--sync-policy automated` = sync when Git changes; `--self-heal` = revert manual/controller drift; `--auto-prune` = delete what Git no longer declares (enable last — it is the only one that deletes). Enable in that order, only once every Application is stably Synced + Healthy. These mutate the **live** object; for a lasting change edit and commit `syncPolicy` in the manifest (the ApplicationSet re-asserts Git on the next reconcile).

```bash
# Set syncPolicy directly on the Application CRD (equivalent low-level operation).
# This is the AS-BUILT auto-sync enabled on ak-products for the CI/CD loop:
# selfHeal on, prune off (Argo won't delete resources dropped from Git).
kubectl -n argocd patch application ak-products --type merge \
  -p '{"spec":{"syncPolicy":{"automated":{"selfHeal":true,"prune":false}}}}'
```
- `patch --type merge` sets `spec.syncPolicy.automated` in one call — the same effect as the `argocd app set` flags above, useful when the `argocd` CLI is not installed. With this enabled, the CD workflow's image-tag commit deploys hands-free. Set `"automated":null` to return to manual sync.

---

## N. CI/CD (GitHub Actions)

> **When you need this:** to provision and inspect the CI/CD federated identity, and to understand what the pipeline now does automatically. The pipelines themselves run in GitHub Actions (`.github/workflows/products-ci.yml`, `products-cd.yml`); concept and setup: [DevOps CI/CD Guide](devops-cicd-guide.md).

### The CD OIDC identity (Terraform)

```bash
cd infrastructure/environments/dev/github-oidc
terragrunt plan          # preview: UAMI id-ak-cicd-dev + 2 federated credentials + AcrPush
terragrunt apply         # create them
```
- The `github-oidc` unit provisions the user-assigned managed identity `id-ak-cicd-dev`, its GitHub federated credentials (subjects `repo:seesathish/AntKart-Src3:ref:refs/heads/master` and `:environment:dev`), and an **AcrPush-only** role assignment on the ACR. It applies after `resource-group` and `container-registry` (see [infrastructure/README](../../infrastructure/README.md)). No cluster access is granted.

```bash
terragrunt output -raw client_id         # -> AZURE_CLIENT_ID       (GitHub repository variable)
terragrunt output -raw tenant_id         # -> AZURE_TENANT_ID       (GitHub repository variable)
terragrunt output -raw subscription_id   # -> AZURE_SUBSCRIPTION_ID (GitHub repository variable)
```
- Read the identity's coordinates to set the three GitHub repository **variables** the CD workflow's `azure/login` uses. `-raw` prints the bare value (no quotes) for copy-paste. These are **identifiers, not secrets** (they name the identity; the OIDC token authenticates as it).

### What the pipeline now does for you (was manual)

```bash
# The CD workflow does this automatically on merge to master — you no longer run it by hand:
az acr login --name acrantkartdev
docker build -f AK.Products/AK.Products.API/Dockerfile -t acrantkartdev.azurecr.io/antkart/products:<sha> .
docker push acrantkartdev.azurecr.io/antkart/products:<sha>
# ...then the workflow bumps .image.tag in deploy/helm/values/products.yaml and Argo CD auto-syncs.
```
- Inside the CD job, `az acr login` runs against the **OIDC-authenticated** session (no `az login` with a secret) and the identity's AcrPush role authorises the push. The manual build/push in [section H](#h-docker) is now the **fallback** — for Products, delivery is the pipeline. The image tag is the **commit SHA** (immutable), and the deploy is a Git commit that Argo CD reconciles ([section M](#m-argo-cd-gitops)).

---

## See also

- [AKS Guide](aks-guide.md) — cluster shape, operator access, workload identity, Helm deployment, ingress/TLS, troubleshooting.
- [GitOps Guide](gitops-guide.md) — Argo CD concept, structure, and adoption runbook (the commands in section M in context).
- [DevOps CI/CD Guide](devops-cicd-guide.md) — the GitHub Actions pipelines the section N commands support.
- [Infrastructure Guide](infrastructure-guide.md) — per-resource provisioning (Understand → Build → Execute → Verify).
- [Container Configuration](container-configuration.md) — the config keys each service reads.
- [deploy/helm/README](../../deploy/helm/README.md) · [deploy/cert-manager/README](../../deploy/cert-manager/README.md) — chart and issuer references.
- [Known Issues Register](../KNOWN_ISSUES.md) · [Platform Roadmap](../ROADMAP.md).
