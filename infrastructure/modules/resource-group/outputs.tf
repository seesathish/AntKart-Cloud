# =============================================================================
# Resource Group module — outputs
# =============================================================================
# A resource group is the parent scope for almost everything else, so later
# modules (networking, registry, key vault, data stores, and so on, from Step 4
# onward) consume these outputs to place their resources in this group.

output "name" {
  description = "The resource group name."
  value       = azurerm_resource_group.this.name
}

output "id" {
  description = "The fully-qualified resource group id."
  value       = azurerm_resource_group.this.id
}

output "location" {
  description = "The region the resource group was created in."
  value       = azurerm_resource_group.this.location
}
