# =============================================================================
# Terragrunt LIVE configuration — dev / Function App (notification hosting)
# =============================================================================
# The deployable instance of the function-app module for the dev environment.
# The module says HOW to build the hosting; this file says WHAT values to use
# and wires in the TWO units it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# --- Multi-dependency wiring -------------------------------------------------
# This unit depends on TWO upstream units. Terragrunt applies BOTH first and
# exposes their outputs here, enforcing the correct order and passing real
# values in — never hardcoded.

# (1) The resource group: where everything is created.
dependency "resource_group" {
  config_path = "../resource-group"

  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# (2) The observability unit: provides the Application Insights connection string
# so the Function App reports telemetry from day one. The mock supplies a
# placeholder so init/plan/validate work before observability has been applied.
dependency "observability" {
  config_path = "../observability"

  mock_outputs = {
    connection_string = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example/"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable function-app module this unit runs.
terraform {
  source = "../../../modules/function-app"
}

# inputs: this environment's values. resource group + location come from the
# resource_group dependency; the App Insights connection string comes from the
# observability dependency.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  function_app_name = "func-antkart-notifications-dev"

  # NOTE: storage account names are GLOBALLY UNIQUE, lowercase alphanumeric,
  # 3-24 chars. If this one is taken, choose another.
  storage_account_name = "stantkartfuncdev"

  app_insights_connection_string = dependency.observability.outputs.connection_string

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
