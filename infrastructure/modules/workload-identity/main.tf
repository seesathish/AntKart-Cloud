# =============================================================================
# Workload Identity module — resources
# =============================================================================
# The secret-less bridge between a pod and Azure. For each service this creates:
#   1. a user-assigned managed identity (UAMI) — a stable Azure identity with its
#      own client id, independent of any host resource (system-assigned
#      identities cannot be federated, which is why a UAMI is required).
#   2. a federated identity credential — trusts the AKS cluster's OIDC issuer for
#      one specific Kubernetes ServiceAccount, so a pod running under that account
#      exchanges its projected service-account token for an Entra token. No secret
#      is stored anywhere; this is what DefaultAzureCredential resolves to in AKS.
#   3. the least-privilege role assignments the service needs — supplied per
#      service by the live unit, so this module invents no role names or scopes.

# --- 1. One user-assigned managed identity per service -----------------------
resource "azurerm_user_assigned_identity" "this" {
  for_each = var.services

  name                = "id-ak-${each.key}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

# --- 2. One federated identity credential per service ------------------------
# subject binds the credential to exactly one ServiceAccount in one namespace:
# only a pod running as system:serviceaccount:<namespace>:<sa> can use this
# identity. audience is the fixed Entra token-exchange audience required by the
# workload-identity webhook.
resource "azurerm_federated_identity_credential" "this" {
  for_each = var.services

  name                = "fic-ak-${each.key}-${var.environment}"
  resource_group_name = var.resource_group_name
  parent_id           = azurerm_user_assigned_identity.this[each.key].id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = var.oidc_issuer_url
  subject             = "system:serviceaccount:${var.namespace}:${each.value.service_account_name}"
}

# --- 3. Least-privilege role assignments -------------------------------------
# Flatten services × their role_assignments into a single map so each grant is a
# discrete resource. The key is service|role|scope, which is unique per service
# (a service never asks for the same role on the same scope twice) and stable
# regardless of list order — so reordering a service's list never forces a
# needless re-create.
locals {
  role_assignments = merge([
    for svc_key, svc in var.services : {
      for ra in svc.role_assignments :
      "${svc_key}|${ra.role_definition_name}|${ra.scope}" => {
        service              = svc_key
        role_definition_name = ra.role_definition_name
        scope                = ra.scope
      }
    }
  ]...)
}

resource "azurerm_role_assignment" "this" {
  for_each = local.role_assignments

  scope                = each.value.scope
  role_definition_name = each.value.role_definition_name
  principal_id         = azurerm_user_assigned_identity.this[each.value.service].principal_id

  # principal_type set explicitly so the provider does not look the principal up —
  # avoids a transient "principal not found" while a just-created UAMI's service
  # principal replicates across Entra (same rationale as the role-assignments unit).
  principal_type = "ServicePrincipal"
}
