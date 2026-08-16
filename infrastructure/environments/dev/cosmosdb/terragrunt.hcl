# =============================================================================
# Terragrunt LIVE configuration — dev / Cosmos DB
# =============================================================================
# The deployable instance of the cosmosdb module for the dev environment. The
# module says HOW to build the account and database; this file says WHAT values
# to use and wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the account lives in the resource group created by the
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

# terraform.source: the reusable cosmosdb module this unit runs.
terraform {
  source = "../../../modules/cosmosdb"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs; the rest are dev's choices.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  # NOTE: account names are GLOBALLY UNIQUE, lowercase, 3-44 chars. If this name
  # is already taken, choose another and update the verification commands.
  account_name = "cosmos-antkart-dev"

  database_name = "antkart-products"

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
