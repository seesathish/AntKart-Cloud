# =============================================================================
# Key Vault module — inputs
# =============================================================================
# This module defines HOW the Key Vault is built. The environment supplies WHAT
# values to use. No environment-specific values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the vault is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the vault (supplied from the Resource Group module's output)."
  type        = string
}

variable "key_vault_name" {
  description = "Name of the Key Vault. NOTE: vault names are GLOBALLY UNIQUE, 3-24 characters, alphanumeric and hyphens (must start with a letter). A recently deleted vault keeps its name reserved during soft-delete retention."
  type        = string
}

variable "tenant_id" {
  description = "Microsoft Entra (Azure AD) tenant id the vault is bound to. Optional: if left null, the module uses the tenant of the identity Terraform is running as (via data.azurerm_client_config), so the environment does not have to hardcode it."
  type        = string
  default     = null
}

variable "sku" {
  description = "Key Vault SKU: standard or premium (premium backs keys with HSMs)."
  type        = string
  default     = "standard"
}

variable "soft_delete_retention_days" {
  description = "Days a deleted vault (and its secrets) remain recoverable before permanent purge. 7 (the minimum) for a disposable dev vault; longer for production."
  type        = number
  default     = 7
}

variable "purge_protection_enabled" {
  description = "When true, a soft-deleted vault CANNOT be purged early — it must wait out the full retention period. This is a ONE-WAY switch: once enabled on a vault, Azure will not allow disabling it, so the setting must match the live vault's actual state. Production sets this true to prevent irreversible loss. A genuinely disposable environment may set it false to allow early purge/recreate during teardown — but a vault that already has it enabled (e.g. kv-antkart-dev) must keep it true."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to the vault, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}

variable "app_insights_connection_string" {
  description = "Application Insights connection string, stored as the vaulted secret 'ApplicationInsights--ConnectionString' (which the services read as the config key ApplicationInsights:ConnectionString). Sourced from the observability unit's output; leave null to not create the secret. NOTE: the identity running Terraform needs the data-plane 'Key Vault Secrets Officer' role on the vault to write this secret (the vault uses RBAC authorization)."
  type        = string
  default     = null
  sensitive   = true
}
