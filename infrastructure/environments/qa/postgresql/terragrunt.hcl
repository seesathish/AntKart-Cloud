# =============================================================================
# Terragrunt LIVE configuration — dev / PostgreSQL Flexible Server
# =============================================================================
# The deployable instance of the postgresql module for the dev environment. The
# module says HOW to build the server and databases; this file says WHAT values
# to use and wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm/azuread/random providers from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the server lives in the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real name and location are wired in — never hardcoded.
#
# NOTE: no key-vault dependency yet. The generated admin password and the
# per-service connection strings are copied into Key Vault in a LATER step;
# this unit only provisions the server and databases. Wiring KV here now would
# couple secret storage to server creation before that step is designed.
dependency "resource_group" {
  config_path = "../resource-group"

  # mock_outputs let init/plan/validate run before the dependency is applied
  # (e.g. on a clean checkout or in CI). A real apply uses the actual outputs.
  mock_outputs = {
    name     = "rg-antkart-qa-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable postgresql module this unit runs.
terraform {
  source = "../../../modules/postgresql"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs; the rest are dev's choices.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  #location            = dependency.resource_group.outputs.location
  # location = dependency.resource_group.outputs.location   
  # eastus — RESTRICTED for Postgres on this subscription
  location = "eastus2"   # Postgres provisioned in the paired region (eastus is offer-restricted here)

  # NOTE: server names are GLOBALLY UNIQUE (part of the hostname). If this one is
  # taken, choose another and update the verification commands.
  server_name = "psql-antkart-qa-eus2"

  # One database per relational service. Kept explicit here (rather than relying
  # on the module default) so the environment's data footprint is visible.
  database_names = ["AKOrdersDb", "AKPaymentsDb", "AKNotificationsDb", "AKDiscountDb"]

  # RFC 5737 documentation address — a deliberate placeholder, not a working value.
  # Set this to the operator's own public IP before applying, or the firewall rule will
  # block all access.
  allowed_client_ip = "203.0.113.10"

  tags = {
    environment = "qa"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
