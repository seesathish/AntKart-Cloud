# =============================================================================
# Role Assignments module — inputs
# =============================================================================
# This module grants ONE principal (a managed identity) the least-privilege
# DATA-plane roles it needs on specific resources. It takes the principal and
# the target resource ids; the live config wires them from the upstream units.

variable "principal_id" {
  description = "Object/principal id of the managed identity being granted access (the Function App's system-assigned identity)."
  type        = string
}

variable "key_vault_id" {
  description = "Resource id of the Key Vault to scope the Secrets User grant to."
  type        = string
}

variable "servicebus_namespace_id" {
  description = "Resource id of the Service Bus namespace to scope the Data Receiver/Sender grants to."
  type        = string
}

variable "eventgrid_topic_id" {
  description = "Resource id of the Event Grid topic to scope the Data Sender grant to."
  type        = string
}
