# ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image

**Status:** Accepted (implemented — amended to reflect the as-built cluster)
**Date:** 2026-07-23
**Area:** Kubernetes Platform
**Relates to:** ADR-016 (Cosmos data migration + workload-identity foundation), ADR-022 (CI/CD on GitHub Actions with OIDC)

> **Amendment note.** This ADR originally recorded the *intended* cluster design. The cluster is now provisioned and running, and several choices were deliberately simplified for a single-developer dev environment. The decisions below describe the **as-built** cluster, with the reasons for each simplification. Where the original intent remains the production target (a hardened base image, a two-pool topology), it is recorded as **future work**, not as done.

---

## Context

This phase moves AntKart services onto managed Kubernetes (AKS). Three decisions are made together because each constrains the others:

1. **How to run the cluster** — sizing, node pools, networking plugin, SKU tier, autoscaling, observability.
2. **How services authenticate to Azure resources inside the cluster** — without secrets, without rotation, with one `DefaultAzureCredential` line of code that works identically on a developer laptop and in AKS.
3. **What base image services build from** — security posture, supply-chain control, and operational debugging.

The guiding principle for the dev environment is **cost predictability and simplicity**: prefer the smallest, cheapest shape that proves the platform end to end, and record the richer production design as future work rather than building it prematurely.

---

## Decisions

### Decision 1 — AKS cluster shape (as-built)

The cluster `aks-antkart-dev` is provisioned by the `aks` Terraform module and its dev unit.

| Aspect | Choice | Rationale |
|--------|--------|-----------|
| **Kubernetes version** | `1.35`, pinned | Pinning makes provisioning reproducible. AKS retires versions over time, so the value is chosen from those marked `KubernetesOfficial` — versions offered only under `AKSLongTermSupport` require an LTS-tier subscription. |
| **Control-plane SKU tier** | `Free` in dev, `Standard` in prod | Free has no SLA but no control-plane cost — right for a disposable dev cluster. Production needs the financially-backed SLA. Per-environment tier selection is a deliberate Well-Architected cost-pillar pattern. |
| **Node pool** | A **single** `system` pool — `2 × Standard_D2s_v3`, **autoscaling disabled** | One fixed-size pool keeps dev cost predictable and the setup simple. `D2s_v3` gives steadier CPU than a burstable B-series (and sidesteps the regional/architecture limits on some smaller SKUs). **Production splits `system`/`user` pools and enables autoscaling** — see Future Work. |
| **Node subnet** | The `aks` subnet, `10.0.0.0/22` | The large subnet the networking module sized for the cluster's nodes (≈1,019 usable IPs). |
| **Network plugin** | **Azure CNI Overlay** (`network_plugin = azure`, `network_plugin_mode = overlay`) | Overlay draws pod IPs from a logical overlay CIDR, so pods do **not** consume VNet address space and the cluster scales without VNet IP pressure — while still using Azure CNI (not kubenet). |
| **Network policy** | `azure` | Network policy is enforced by the Azure CNI itself, so no separate Calico add-on is installed. Calico remains an option if a feature it uniquely provides is later needed. |
| **Pod / service CIDRs** | pod `10.244.0.0/16`, service `10.245.0.0/16`, DNS `10.245.0.10` | Chosen not to overlap the VNet (`10.0.0.0/16`) or each other. |
| **AKS API server** | Public (Free-tier limitation) | Private AKS requires Standard tier. Public + Azure RBAC + Entra integration is acceptable for dev. |
| **Authorization** | **Azure RBAC** for Kubernetes enabled; **local accounts left enabled** | `kubectl` access is granted with an Azure role (`Azure Kubernetes Service RBAC Cluster Admin`). Local accounts are intentionally left enabled for now so we cannot lock ourselves out before Entra access is fully established. |
| **Image pulls** | **AcrPull** on the **kubelet** identity | The actual image puller is the kubelet identity (a common AKS gotcha is granting it to the control-plane identity instead). Verified: an image pulled successfully with **no `imagePullSecret`**. |
| **Control-plane identity** | `SystemAssigned` | Azure manages its lifecycle with the cluster. |
| **Observability** | OMS agent → existing Log Analytics workspace | Reuse the existing workspace; managed-identity auth, no shared keys. |

### Decision 2 — Workload Identity for secret-less Azure access (as-built)

| Aspect | Choice | Rationale |
|--------|--------|-----------|
| **Cluster flags** | `oidc_issuer_enabled = true` + `workload_identity_enabled = true`, **set at creation** | The two flags expose the OIDC issuer and project service-account tokens into pods. Enabling them **after** creation forces a cluster update, so they are set at creation time. |
| **Federation scope** | **One identity per deployed service** (all six) | Each service that runs in the cluster gets its own identity. Identities are named `id-ak-<service>-dev`. |
| **Identity type** | User-Assigned Managed Identity (UAMI) | Required for federation — system-assigned identities are tied to a host resource and cannot be pre-created/federated. UAMIs are independent resources with stable client ids. |
| **Federation pattern** | One UAMI per service, one federated credential per UAMI, subject `system:serviceaccount:antkart:ak-<service>`, audience `api://AzureADTokenExchange` | The subject is **exact-match and case-sensitive**; a mismatch fails as `AADSTS70021` / "workload options are not fully configured". Each service's identity is independent — a compromise of one does not propagate. |
| **Least privilege** | Per-service role matrix (below) | Each identity gets only the data-plane roles its service needs. |
| **Code change required in services** | **None** | Application code already uses `DefaultAzureCredential`; it resolves to `AzureCliCredential` locally and `WorkloadIdentityCredential` in AKS — same line of code, different provider in the chain. |

**Role matrix.** Cosmos DB, PostgreSQL, and Redis are reached via connection strings stored in Key Vault, so access to them is granted transitively through **Key Vault Secrets User** rather than a data-plane role on those stores.

| Service | Roles granted → scope |
|---------|-----------------------|
| Products | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| ShoppingCart (`ak-cart`) | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| Order | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| Payments | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| Discount | Key Vault Secrets User → vault |
| Gateway | Key Vault Secrets User → vault |

The pod wiring (ServiceAccount annotation + pod-template label) and the verified pass/fail behaviour are documented in the [AKS Guide](../guides/aks-guide.md#workload-identity).

### Decision 3 — Base image

| Aspect | Choice (as-built) | Rationale |
|--------|-------------------|-----------|
| **Runtime base image** | Standard `mcr.microsoft.com/dotnet/aspnet:9.0` | Ships a working, verified fleet first. The images build from the SDK image and run on the ASP.NET runtime image. |
| **Non-root user** | UID 1654 via `USER $APP_UID` | Containers never run as root; every service listens on port 8080 (a non-root user cannot bind ports below 1024). |
| **Hardened / chiseled base** | **Future work — not done** | A hardened, minimal (chiseled) organisation-owned base image remains the intended target for supply-chain control and a smaller attack surface, added by a later security-hardening step. It is **not** part of the current cluster. |

---

## Considered Alternatives

### Alternative 1 — Service-account secrets instead of Workload Identity

Mount Azure service-principal credentials as Kubernetes Secrets and set `AZURE_CLIENT_SECRET` on each pod.

**Rejected** because secrets must be rotated and can leak (logs, image layers, etcd backups, `kubectl get secret -o yaml`). Workload Identity has zero-rotation by design and is the modern path `DefaultAzureCredential` already supports.

### Alternative 2 — Two node pools in dev

A `system` pool (tainted `CriticalAddonsOnly`) plus a separate `user` pool for app workloads.

**Deferred to production, not used in dev.** Two pools give clean isolation of kube-system from app pods and independent scaling — the right production topology (recorded in Future Work). In a single-developer dev cluster it doubles the idle node cost for isolation that dev does not need, so dev runs a single `system` pool.

### Alternative 3 — `kubenet` network plugin

One IP per node, pods NAT'd to the node IP.

**Rejected** in favour of Azure CNI (Overlay). kubenet cannot apply NSG rules to pod traffic and complicates direct pod-to-private-endpoint routing. Azure CNI Overlay keeps Azure CNI's addressing model while avoiding per-pod VNet IP consumption.

### Alternative 4 — Calico network policy

Install Calico for `NetworkPolicy` enforcement.

**Not used.** `network_policy = azure` enforces policy in the Azure CNI itself, one fewer add-on to run and upgrade. Calico remains available if a Calico-specific capability is later required.

### Alternative 5 — Standard SKU tier in dev

`sku_tier = "Standard"` everywhere.

**Rejected** — paying for an uptime SLA that does not matter in a single-developer dev cluster. Per-environment tier selection is the Well-Architected cost pillar in practice.

### Alternative 6 — Chiseled base image now

Adopt a chiseled base image immediately.

**Deferred.** The standard ASP.NET runtime image ships a working fleet first; base-image hardening (chiseled, organisation-owned, scan-and-sign gated) is sequenced as its own step so it can be validated in isolation. Non-root execution is already in place, so the current images are not unhardened, only not-yet-minimised.

---

## Consequences

**Operationally:**
- `kubectl` access requires an Azure role assignment on the cluster and `kubelogin` conversion of the kubeconfig — see the [AKS Guide](../guides/aks-guide.md#operator-access).
- The node pool bills hourly; `az aks stop` / `az aks start` is the routine for idle periods, documented in the [AKS Guide](../guides/aks-guide.md#cost-and-idle-management).
- Adding a new service's workload identity is a repeatable pattern (create UAMI → grant least-privilege roles → create a federated credential for its ServiceAccount → set the client-id annotation on the chart). Encoded in the `workload-identity` module.

**Architecturally:**
- Application code is unchanged. The same `DefaultAzureCredential` runs locally with `az login` and in AKS with Workload Identity — the difference is which credential provider in the chain succeeds.
- The single node pool, disabled autoscaling, and `azure` network policy are **per-environment dev choices**, not permanent design. Production overrides to a two-pool topology with autoscaling and the Standard SKU tier.
- Base-image hardening is deferred, so the platform team's hardening surface (a chiseled, scan-gated base image) is a known future step rather than a current guarantee.

**Financially:**
- Dev compute is `2 × Standard_D2s_v3` billed hourly while the cluster runs; `az aks stop` when idle brings node billing to ~$0. Free control-plane tier adds no control-plane cost.

---

## Future Work

- **Two-pool topology + autoscaling** for production (system pool tainted `CriticalAddonsOnly`, separate user pool, per-pool autoscaler).
- **Hardened base image** — a chiseled, organisation-owned base image in ACR, with a scan-and-signed-digest admission gate.
- **Private API server** and the Standard SKU tier for production.
