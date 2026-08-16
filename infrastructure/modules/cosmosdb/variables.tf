# =============================================================================
# Cosmos DB module — inputs
# =============================================================================
# This module defines HOW the Cosmos DB account (serverless, MongoDB API) and
# its database are built. The environment supplies WHAT values to use. No
# environment values baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the account is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Cosmos DB account (supplied from the Resource Group module's output). A serverless account is single-region."
  type        = string
}

variable "account_name" {
  description = "Name of the Cosmos DB account. NOTE: account names are GLOBALLY UNIQUE, lowercase letters/numbers/hyphens, 3-44 characters (it becomes part of the endpoint hostname)."
  type        = string
}

variable "database_name" {
  description = "Name of the MongoDB database created in the account (e.g. antkart-products)."
  type        = string
}

variable "mongo_server_version" {
  description = "MongoDB wire-protocol version the account presents (e.g. 7.0). Existing MongoDB driver code targets this version."
  type        = string
  default     = "7.0"
}

variable "tags" {
  description = "Tags applied to the account, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
