# =============================================================================
# Governance module — monthly budget + cost alerts (FinOps)
# =============================================================================
# FinOps in one idea: spend should never SURPRISE you. A budget does not cap or
# stop spending — it WATCHES it and emails you as it approaches a limit, so you
# react before the bill arrives. Proactive cost alerting turns "why is the bill
# huge?" into "we were told at 50%, 80%, and on forecast."

resource "azurerm_consumption_budget_resource_group" "this" {
  name              = var.budget_name
  resource_group_id = var.resource_group_id

  # A monthly budget: the amount resets at the start of each month.
  amount     = var.amount
  time_grain = "Monthly"

  time_period {
    # When the budget begins evaluating. Must be the first of a month.
    start_date = var.start_date
  }

  # One notification per threshold. Each emails the contacts when the chosen
  # measure (Actual or Forecasted spend) reaches the percentage of the budget.
  dynamic "notification" {
    for_each = var.notifications
    content {
      enabled        = true
      threshold      = notification.value.threshold
      threshold_type = notification.value.threshold_type
      operator       = "GreaterThanOrEqualTo"
      contact_emails = var.contact_emails
    }
  }
}
