# ADR-013 — Key Vault RBAC Authorization, Workspace-Based App Insights, and ACR Basic-to-Premium Strategy

**Status:** Accepted
**Date:** 2026-05-30
**Author:** Sathish Chandrakumar

---

## Context

Phase 2 provisions four Azure services: Azure Container Registry (ACR),
Azure Key Vault, Azure Log Analytics Workspace, and Azure Application Insights.
Three architectural decisions made during their design require explicit documentation
because each has a commonly-used alternative that was rejected, and future maintainers
should understand why.

1. **Key Vault authorization model**: Azure Key Vault supports two authorization systems (RBAC and Access Policies). Choosing the wrong one requires a vault-level migration later.
2. **Application Insights data model**: App Insights has a "classic" and "workspace-based" mode. Classic is retired; workspace-based is required for new resources.
3. **ACR SKU strategy**: Starting at Basic SKU is cost-effective but excludes private endpoints, which are required for a fully private AKS cluster. The upgrade path must be designed upfront.

---

## Decision 1 — Key Vault: Azure RBAC over Access Policies

### Chosen approach

`rbac_authorization_enabled = true` on all Key Vault resources.

Role assignments manage access:
- Deploying SP: `Key Vault Secrets Officer` (manage secrets — scoped to the vault)
- AKS node managed identity: `Key Vault Secrets User` (read-only — assigned in the AKS module)

### Rejected alternative: Access Policies

Access Policies is Key Vault's original authorization system, predating Azure RBAC's generalization.

**Why rejected:**

| Concern | Access Policies | Azure RBAC |
|---------|-----------------|------------|
| Visibility | Separate "Access policies" blade — invisible in IAM | Standard IAM blade — consistent with all Azure resources |
| Granularity | Vault-level only | Secret-level scope possible |
| Conditional Access | Not evaluated | Supported |
| PIM (privileged identity management) | Not supported | Supported |
| Consistency | Vault-specific model | Same model as all Azure resources |

Access Policies is not deprecated, but it is the legacy path. All new Azure Landing Zone guidance recommends RBAC. Choosing RBAC now avoids a migration (which requires re-granting all access) when Conditional Access or PIM is required in a later compliance review.

**Accepted tradeoff**: `rbac_authorization_enabled = true` is an irreversible change on a vault. Once set, access policies are ignored and cannot be re-enabled without recreating the vault. This is acceptable because we are choosing RBAC deliberately and will not revert.

---

## Decision 2 — Application Insights: Workspace-Based Mode

### Chosen approach

All `azurerm_application_insights` resources include `workspace_id` pointing to the shared Log Analytics Workspace. This activates workspace-based mode.

### Rejected alternative: Classic (standalone) App Insights

Classic App Insights stores telemetry in a private, opaque per-resource backend.

**Why rejected:**

1. **Cross-service correlation is impossible.** With 8 microservices, understanding a request that touches Products → ShoppingCart → Order → Payments → Notification requires querying 5 separate App Insights resources. In workspace-based mode, one KQL query covers all services.

2. **Microsoft retired Classic App Insights on 29 February 2024.** Azure stopped accepting new Classic App Insights resources after this date. Attempting to create one without `workspace_id` will result in a deployment error.

3. **Shared cost management.** The 5 GB/month free tier applies at the workspace level — one workspace covering 8 microservices is more economical than 8 separate Classic instances.

**Accepted tradeoff**: Workspace-based mode requires the Log Analytics Workspace to exist before Application Insights. This creates a deployment ordering dependency (`log-analytics → app-insights`) enforced by Terragrunt's `dependency` block in the environment wiring.

---

## Decision 3 — ACR: Basic SKU Now, Premium Later

### Chosen approach

Start with `sku = "Basic"` in dev. Design the module so upgrading to Premium for private endpoints requires only a one-line change and an `apply`.

### Rejected alternatives

**Option A: Start with Premium immediately.**
Cost: ~$50/month for dev (vs. $5 for Basic). Premium features (private endpoints, geo-replication) are not needed until the AKS cluster is hardened into a fully private configuration. Starting with Premium pays for features we don't use yet.

**Option B: Docker Hub with a private plan.**
Docker Hub's private registry plan is cheaper (~$9/month for a team) but:
- Images are hosted outside Azure — each AKS pull crosses the public internet and incurs egress charges
- No integration with Azure managed identity — credentials required for every pull
- No Azure Private Link support — cannot be made zero-egress

### Upgrade path (documented for the team)

When the AKS cluster is made private (a later hardening step):

1. Change `sku = "Premium"` in `environments/{env}/acr/terragrunt.hcl`
2. Run `terragrunt apply` on the ACR module — this is an in-place update (`~`), no recreation
3. Add an `azurerm_private_endpoint` resource on the `pe_subnet_id` from the networking module
4. Add a Private DNS Zone for `privatelink.azurecr.io` linked to the VNet
5. AKS pulls images via private IP — no public internet traversal

The module structure requires no changes for steps 3-4 — they are additive resources.

---

## Consequences

### Positive

- **Key Vault RBAC**: access control is visible, auditable, and consistent with the rest of the platform. Secret-level scope is available when needed for zero-trust scenarios.
- **Workspace-based App Insights**: end-to-end request traces across all 8 microservices are possible from day one. No future migration needed when cross-service analytics are required.
- **ACR Basic**: minimal cost during active development. The upgrade to Premium is one `terragrunt apply` away when private networking is required.

### Negative

- **Key Vault RBAC**: `rbac_authorization_enabled = true` is irreversible on an existing vault. Reverting requires vault recreation and re-granting all access. This is acceptable as a deliberate choice.
- **Log Analytics ordering dependency**: `app-insights` cannot be deployed before `log-analytics`. This adds one step to the initial provisioning order and to any fresh environment build.
- **ACR Basic**: no private endpoint support. During dev, ACR traffic traverses the public internet. ACR does enforce TLS and registry authentication — images are not publicly readable. This is acceptable risk for a dev environment.

---

## References

- [Azure Key Vault RBAC documentation](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide)
- [Migrate Key Vault from access policies to RBAC](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-migration)
- [Workspace-based Application Insights](https://learn.microsoft.com/en-us/azure/azure-monitor/app/create-workspace-resource)
- [Classic App Insights retirement announcement](https://azure.microsoft.com/en-us/updates/we-re-retiring-classic-application-insights-on-29-february-2024/)
- [ACR SKU comparison](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-skus)
- [ACR private link with AKS](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-private-link)
- ADR-012 — Infrastructure as Code with Terraform and Terragrunt
