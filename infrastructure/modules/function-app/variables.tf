# =============================================================================
# Function App module — inputs
# =============================================================================
# This module defines HOW the serverless Function App home is built: a
# Consumption hosting plan, a dedicated runtime storage account, and the
# Function App shell. The environment supplies WHAT values to use.

variable "resource_group_name" {
  description = "Name of the resource group these resources are created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Function App and its supporting resources (supplied from the Resource Group module's output)."
  type        = string
}

variable "function_app_name" {
  description = "Name of the Function App (the home application code is deployed into later)."
  type        = string
}

variable "storage_account_name" {
  description = "Name of the storage account dedicated to the Functions runtime. NOTE: storage account names are GLOBALLY UNIQUE, lowercase alphanumeric only, 3-24 characters."
  type        = string
}

variable "app_insights_connection_string" {
  description = "Application Insights connection string (from the observability unit) the Function App uses to send telemetry from day one."
  type        = string
  sensitive   = true
}

variable "tags" {
  description = "Tags applied to the resources, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
