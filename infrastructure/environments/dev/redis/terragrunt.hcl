# =============================================================================
# Terragrunt LIVE configuration — dev / Azure Cache for Redis
# =============================================================================
# The deployable instance of the redis module for the dev environment. The
# module says HOW to build the cache; this file says WHAT values to use and
# wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared providers from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the cache lives in the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real name is wired in — never hardcoded.
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

# terraform.source: the reusable redis module this unit runs.
terraform {
  source = "../../../modules/redis"
}

# inputs: this environment's values. The resource group name comes from the
# dependency's output; the rest are dev's choices.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name

  # location is HARDCODED (not taken from the resource-group output) to keep the
  # cache in the same region as Postgres: eastus is offer-restricted on this
  # subscription, so the data services are provisioned in the paired region.
  #
  # CAPACITY NOTE: Azure Managed Redis can fail to allocate in a region that is
  # temporarily at capacity (apply error: "AllocationFailed"). If that happens,
  # this region may need to change (try another nearby region) and re-apply.
  location = "eastus2"

  # NOTE: cache names are GLOBALLY UNIQUE (part of the hostname). If this one is
  # taken, choose another and update the verification commands.
  name = "redis-antkart-dev"

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
