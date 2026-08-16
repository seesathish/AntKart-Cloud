# =============================================================================
# Terragrunt LIVE configuration — dev / Governance (budget + cost alerts)
# =============================================================================
# The deployable instance of the governance module for the dev environment.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared provider/version generation from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the budget is scoped to the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real resource group id is wired in — never hardcoded.
dependency "resource_group" {
  config_path = "../resource-group"

  # mock_outputs let init/plan/validate run before the dependency is applied.
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-qa-eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable governance module this unit runs.
terraform {
  source = "../../../modules/governance"
}

# inputs: this environment's values.
inputs = {
  resource_group_id = dependency.resource_group.outputs.id

  budget_name = "budget-antkart-qa"
  amount      = 200
  start_date  = "2026-08-01T00:00:00Z"

  # REPLACE with the real contact address before applying — this is who receives
  # the cost alerts. Must be set per environment.
  contact_emails = ["your-email@example.com"]
}
