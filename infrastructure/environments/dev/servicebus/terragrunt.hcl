# =============================================================================
# Terragrunt LIVE configuration — dev / Service Bus
# =============================================================================
# The deployable instance of the servicebus module for the dev environment. The
# module says HOW to build the namespace and entities; this file says WHAT
# values to use and wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the namespace lives in the resource group created by the
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

# terraform.source: the reusable servicebus module this unit runs.
terraform {
  source = "../../../modules/servicebus"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs. The queue (order-commands) and topic (integration-events)
# keep the module defaults; subscription_names lists ONE subscription per consuming
# service on the integration-events topic — each service reads its own subscription.
# Adding a new consuming service is an infrastructure change: add its subscription here.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  # NOTE: namespace names are GLOBALLY UNIQUE (part of the hostname). If this one
  # is taken, choose another and update the verification commands.
  namespace_name = "sb-antkart-dev"

  # One subscription per consuming service on the integration-events topic.
  # (The "notification" subscription was removed when notifications moved to the serverless
  # Event Grid + Functions model — notifications no longer consume from Service Bus.)
  subscription_names = ["products", "order", "payments", "cart"]

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
