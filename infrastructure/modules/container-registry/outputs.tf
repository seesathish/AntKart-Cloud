# =============================================================================
# Container Registry module — outputs
# =============================================================================
# Later steps consume these: AKS needs the registry id to grant its kubelet
# identity the AcrPull role; build/deploy steps need the login server to tag and
# push images.

output "id" {
  description = "The container registry resource id (used to scope the AcrPull role assignment for AKS)."
  value       = azurerm_container_registry.this.id
}

output "name" {
  description = "The container registry name."
  value       = azurerm_container_registry.this.name
}

output "login_server" {
  description = "The registry login server hostname (e.g. acrantkartdev.azurecr.io), used to tag and push images."
  value       = azurerm_container_registry.this.login_server
}
