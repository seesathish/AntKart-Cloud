# =============================================================================
# Communication Services module — inputs
# =============================================================================
# This module defines HOW Azure Communication Services (ACS) Email is built. The
# environment supplies WHAT values to use. No environment values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the ACS resources are created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "name_prefix" {
  description = "Prefix used to derive the ACS resource names, e.g. \"antkart-dev\" yields \"acs-antkart-dev\" (Communication Service) and \"acs-email-antkart-dev\" (Email Communication Service)."
  type        = string
}

variable "data_location" {
  description = "Geography where ACS stores its data AT REST. This is a residency choice, not a deployment region (ACS is a global service). Allowed values include: United States, Europe, Australia, UK, etc. Changing it forces the resources to be recreated."
  type        = string
  default     = "United States"
}

variable "tags" {
  description = "Tags applied to the ACS resources, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
