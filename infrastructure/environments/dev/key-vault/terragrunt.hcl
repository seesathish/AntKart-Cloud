# =============================================================================
# Terragrunt LIVE configuration — dev / Key Vault
# =============================================================================
# The deployable instance of the key-vault module for the dev environment. The
# module says HOW to build the vault; this file says WHAT values to use and
# wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the vault lives in the resource group created by the
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

# dependency: the Application Insights connection string this vault stores as a secret
# comes from the observability unit's output. Terragrunt applies observability FIRST and
# exposes its outputs here, so the real value is wired in — never hardcoded.
dependency "observability" {
  config_path = "../observability"

  # mock_outputs let init/plan/validate run before the dependency is applied. A real
  # apply uses the actual connection string.
  mock_outputs = {
    connection_string = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable key-vault module this unit runs.
terraform {
  source = "../../../modules/key-vault"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs. The tenant id is intentionally NOT set here — the
# module resolves it from the current Azure context, so nothing is hardcoded.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  # NOTE: vault names are GLOBALLY UNIQUE, 3-24 chars, alphanumeric/hyphens. If
  # this name was used before and soft-deleted, it may still be reserved during
  # the retention window — recover/purge it or choose a new name.
  key_vault_name = "kv-antkart-dev"

  # Purge protection was ENABLED out of band on the live kv-antkart-dev vault, and
  # Azure does NOT allow disabling it once on ("once Purge Protection has been Enabled
  # it's not possible to disable it"). This records that reality — it is not a preference
  # for dev. Leaving it false here would make every apply try to disable it and fail.
  # See docs/KNOWN_ISSUES.md (KI-007) for the teardown/rebuild consequence.
  purge_protection_enabled = true

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }

  # Vault the Application Insights connection string as the secret
  # ApplicationInsights--ConnectionString, which the six AKS services read as the config
  # key ApplicationInsights:ConnectionString. Sourced from the observability unit; the
  # value is sensitive and is NEVER placed in a values file, Helm chart, or committed YAML.
  app_insights_connection_string = dependency.observability.outputs.connection_string
}
