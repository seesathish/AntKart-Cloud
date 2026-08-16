# =============================================================================
# Resource Group module — inputs
# =============================================================================
# This module defines HOW a resource group is built. These variables are the
# knobs the calling environment turns. There are no defaults that bake in
# environment-specific values — name and location are always supplied by the
# caller, so the same module produces a structurally identical resource group
# in every environment.

variable "name" {
  description = "Name of the resource group (e.g. rg-antkart-dev-eastus)."
  type        = string
}

variable "location" {
  description = "Azure region in which the resource group is created (e.g. eastus)."
  type        = string
}

variable "tags" {
  description = "Tags applied to the resource group for ownership, cost grouping, and lifecycle. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
