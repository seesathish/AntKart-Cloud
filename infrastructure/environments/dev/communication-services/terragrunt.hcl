# =============================================================================
# Terragrunt LIVE configuration — dev / Communication Services (ACS Email)
# =============================================================================
# The deployable instance of the communication-services module for the dev
# environment. The module says HOW to build ACS Email; this file says WHAT
# values to use and wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth), the
# shared azurerm provider, and the shared provider version pin from
# environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the ACS resources live in the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real name is wired in — never hardcoded. (ACS is a global
# service and takes no location argument; only the resource group name is needed.)
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

# terraform.source: the reusable communication-services module this unit runs.
terraform {
  source = "../../../modules/communication-services"
}

# inputs: this environment's values. The resource group name comes from the
# dependency's output; the name prefix and data residency are dev's choice.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name

  # Yields "acs-antkart-dev" (Communication Service) and
  # "acs-email-antkart-dev" (Email Communication Service).
  name_prefix = "antkart-dev"

  # Data-at-rest residency for ACS. United States is the default and is fine for
  # the platform's purposes; change it for a different residency requirement.
  data_location = "United States"

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
