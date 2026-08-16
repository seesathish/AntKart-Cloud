# =============================================================================
# Container Registry module — inputs
# =============================================================================
# This module defines HOW the Azure Container Registry is built. The environment
# supplies WHAT values to use. No environment-specific values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the registry is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the registry (supplied from the Resource Group module's output)."
  type        = string
}

variable "acr_name" {
  description = "Name of the container registry. NOTE: ACR names are GLOBALLY UNIQUE, alphanumeric only (no hyphens), 5-50 characters. If the chosen name is taken, pick another."
  type        = string
}

variable "sku" {
  description = "ACR pricing tier: Basic, Standard, or Premium. Basic is used for dev; Premium adds private endpoints, geo-replication, and content trust for production."
  type        = string
  default     = "Basic"
}

variable "tags" {
  description = "Tags applied to the registry, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
