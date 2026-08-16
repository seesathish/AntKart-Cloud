# =============================================================================
# Networking module — outputs
# =============================================================================
# Later modules build inside this network and need to reference it. The AKS
# module needs its subnet id; the private-endpoints module needs that subnet id;
# the gateway module needs the gateway subnet id. They all consume subnet_ids.

output "vnet_id" {
  description = "The virtual network resource id."
  value       = azurerm_virtual_network.this.id
}

output "vnet_name" {
  description = "The virtual network name."
  value       = azurerm_virtual_network.this.name
}

output "subnet_ids" {
  description = "Map of subnet name => subnet id, consumed by later modules (AKS, private endpoints, gateway)."
  value       = { for name, subnet in azurerm_subnet.this : name => subnet.id }
}
