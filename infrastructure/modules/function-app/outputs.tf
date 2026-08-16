# =============================================================================
# Function App module — outputs
# =============================================================================
# Later steps reference these. principal_id is the system-assigned managed
# identity, consumed by the role-assignment step to grant data-plane access.
# No keys or connection strings are output.

output "id" {
  description = "The Function App resource id."
  value       = azurerm_linux_function_app.this.id
}

output "name" {
  description = "The Function App name."
  value       = azurerm_linux_function_app.this.name
}

output "default_hostname" {
  description = "The Function App's default hostname (e.g. func-antkart-notifications-dev.azurewebsites.net)."
  value       = azurerm_linux_function_app.this.default_hostname
}

output "principal_id" {
  description = "The system-assigned managed identity's principal id, used to grant this Function App data-plane roles in a later step."
  value       = azurerm_linux_function_app.this.identity[0].principal_id
}
