# =============================================================================
# Key Vault module — outputs
# =============================================================================
# Later steps and applications consume these: the id scopes RBAC role
# assignments (e.g. Key Vault Secrets User for app identities); the vault_uri is
# the endpoint applications call to read secrets at runtime.

output "id" {
  description = "The Key Vault resource id (used to scope RBAC role assignments)."
  value       = azurerm_key_vault.this.id
}

output "name" {
  description = "The Key Vault name."
  value       = azurerm_key_vault.this.name
}

output "vault_uri" {
  description = "The vault endpoint URI (e.g. https://kv-antkart-dev.vault.azure.net/) that applications use to read secrets."
  value       = azurerm_key_vault.this.vault_uri
}
