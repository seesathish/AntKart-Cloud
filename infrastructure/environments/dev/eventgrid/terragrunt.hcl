# =============================================================================
# Terragrunt LIVE configuration — dev / Event Grid
# =============================================================================
# The deployable instance of the eventgrid module for the dev environment. The
# module says HOW to build the topic; this file says WHAT values to use and
# wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the topic lives in the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real name and location are wired in — never hardcoded.
dependency "resource_group" {
  config_path = "../resource-group"

  # mock_outputs let init/plan/validate run before the dependency is applied
  # (e.g. on a clean checkout or in CI). A real apply uses the actual outputs.
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable eventgrid module this unit runs.
terraform {
  source = "../../../modules/eventgrid"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs; the topic name is dev's choice.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  topic_name = "evgt-antkart-dev"

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
