# =============================================================================
# Governance module — outputs
# =============================================================================

output "budget_id" {
  description = "The consumption budget resource id."
  value       = azurerm_consumption_budget_resource_group.this.id
}

output "budget_name" {
  description = "The consumption budget name."
  value       = azurerm_consumption_budget_resource_group.this.name
}
