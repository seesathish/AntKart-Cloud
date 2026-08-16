# =============================================================================
# Terragrunt LIVE configuration — dev / Resource Group
# =============================================================================
# This is a "unit": the deployable instance of the Resource Group module for the
# dev environment. The module says HOW to build a resource group; this file says
# WHAT values to use here, and wires in the shared backend/provider via the root.

# include "root": pulls in environments/dev/root.hcl, so this unit inherits the
# remote state backend (Azure AD auth) and the shared azurerm provider.
# find_in_parent_folders walks up the directory tree until it finds root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# terraform.source: which module this unit runs. The path is relative to this
# file and points at the reusable Resource Group module under infrastructure/modules.
terraform {
  source = "../../../modules/resource-group"
}

# inputs: this environment's values for the module's variables. These are the
# only environment-specific values; the module itself stays generic.
inputs = {
  name     = "rg-antkart-dev-eastus"
  location = "eastus"

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
