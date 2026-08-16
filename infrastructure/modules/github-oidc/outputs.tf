# =============================================================================
# GitHub OIDC (CI/CD) module — outputs
# =============================================================================
# These become the values the CD workflow's azure/login step uses:
#   client_id       -> AZURE_CLIENT_ID
#   tenant_id       -> AZURE_TENANT_ID
#   subscription_id -> AZURE_SUBSCRIPTION_ID
# NONE of these are secrets. They are identifiers, not credentials — the identity
# holds no key to leak; trust comes entirely from the OIDC federation (the subject
# match), so they are safe to store as plain GitHub Actions *variables* rather than
# secrets. (There is no client secret to output, by design.)

output "client_id" {
  description = "Client id of the CI/CD user-assigned managed identity. Set as AZURE_CLIENT_ID in GitHub (a variable, not a secret)."
  value       = azurerm_user_assigned_identity.this.client_id
}

output "tenant_id" {
  description = "Entra tenant id the identity lives in. Set as AZURE_TENANT_ID in GitHub (a variable, not a secret)."
  value       = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  description = "Subscription the identity and ACR live in. Set as AZURE_SUBSCRIPTION_ID in GitHub (a variable, not a secret)."
  value       = data.azurerm_client_config.current.subscription_id
}

output "identity_name" {
  description = "Name of the CI/CD user-assigned managed identity (id-ak-cicd-<environment>)."
  value       = azurerm_user_assigned_identity.this.name
}

output "principal_id" {
  description = "Principal (object) id of the identity's service principal — the assignee of the AcrPush role. An identifier, not a secret."
  value       = azurerm_user_assigned_identity.this.principal_id
}

output "federated_subjects" {
  description = "The exact-match OIDC subjects trusted, keyed by their short name. These must equal the `sub` claim GitHub presents."
  value       = local.federated_subjects
}
