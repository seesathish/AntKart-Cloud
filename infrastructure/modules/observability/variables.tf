# =============================================================================
# Observability module — inputs
# =============================================================================
# This module defines HOW the observability destination (a Log Analytics
# workspace plus a workspace-based Application Insights) is built. The
# environment supplies WHAT values to use. No environment values baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the workspace and App Insights are created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the observability resources (supplied from the Resource Group module's output)."
  type        = string
}

variable "log_analytics_name" {
  description = "Name of the Log Analytics workspace (the central telemetry store)."
  type        = string
}

variable "app_insights_name" {
  description = "Name of the Application Insights component (the application performance monitoring layer)."
  type        = string
}

variable "retention_days" {
  description = "How many days telemetry is retained in the workspace. 30 is the workspace minimum and sits within the free retention window — a sensible, low-cost choice for dev; production typically retains longer."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags applied to the observability resources, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
