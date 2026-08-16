# =============================================================================
# Service Bus module — outputs
# =============================================================================
# The application connects to Service Bus with its OWN identity (token auth),
# using the namespace hostname below — there are no connection strings here.
# Data-plane access (send/receive) is granted to application identities via
# Service Bus RBAC roles in the managed-identity step; the app authenticates
# with its identity, never a SAS key.

output "id" {
  description = "The Service Bus namespace resource id (used to scope data-plane RBAC role assignments)."
  value       = azurerm_servicebus_namespace.this.id
}

output "name" {
  description = "The Service Bus namespace name."
  value       = azurerm_servicebus_namespace.this.name
}

output "hostname" {
  description = "The fully-qualified namespace hostname (<name>.servicebus.windows.net) the application uses for its identity-based (token) connection."
  value       = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"
}
