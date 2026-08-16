# =============================================================================
# Governance module — inputs
# =============================================================================
# This module adds a monthly cost BUDGET with alert thresholds — the FinOps
# control that makes spend visible before it becomes a surprise.

variable "resource_group_id" {
  description = "Resource id of the resource group the budget is scoped to (spend within this group counts toward the budget)."
  type        = string
}

variable "budget_name" {
  description = "Name of the consumption budget."
  type        = string
}

variable "amount" {
  description = "Monthly budget amount in the billing currency (US$)."
  type        = number
  default     = 200
}

variable "start_date" {
  description = "Budget start date (RFC3339). MUST be the first day of a month, e.g. 2026-06-01T00:00:00Z."
  type        = string
  default     = "2026-06-01T00:00:00Z"
}

variable "contact_emails" {
  description = "Email addresses notified when a threshold is reached."
  type        = list(string)
}

# The thresholds at which to alert, as a percentage of the budget. "Actual" fires
# when real spend crosses the line; "Forecasted" fires when projected spend is on
# track to cross it (an early warning).
variable "notifications" {
  description = "Alert thresholds: percentage of budget and whether to evaluate Actual or Forecasted spend."
  type = list(object({
    threshold      = number
    threshold_type = string # "Actual" or "Forecasted"
  }))
  default = [
    { threshold = 50, threshold_type = "Actual" },
    { threshold = 80, threshold_type = "Actual" },
    { threshold = 100, threshold_type = "Forecasted" },
  ]
}
