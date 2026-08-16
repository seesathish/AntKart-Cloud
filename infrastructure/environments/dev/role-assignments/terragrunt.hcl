# =============================================================================
# Terragrunt LIVE configuration — dev / Role Assignments
# =============================================================================
# This unit COMPOSES the outputs of several upstream units: it takes the Function
# App's managed identity and grants it least-privilege data-plane roles on the
# Key Vault, the Service Bus namespace, and the Event Grid topic. It is the unit
# that wires the secret-less model together at the end.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# --- Upstream dependencies ---------------------------------------------------
# Four dependencies, one per output this unit needs. Terragrunt applies all of
# them FIRST and passes their real outputs in. Each mock lets init/plan/validate
# run before the upstream units have been applied.

# The managed identity to grant (the Function App's system-assigned identity).
dependency "function_app" {
  config_path = "../function-app"
  mock_outputs = {
    principal_id = "00000000-0000-0000-0000-000000000000"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The Key Vault to scope the Secrets User grant to.
dependency "key_vault" {
  config_path = "../key-vault"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.KeyVault/vaults/kv-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The Service Bus namespace to scope the Data Receiver/Sender grants to.
dependency "servicebus" {
  config_path = "../servicebus"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.ServiceBus/namespaces/sb-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The Event Grid topic to scope the Data Sender grant to.
dependency "eventgrid" {
  config_path = "../eventgrid"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.EventGrid/topics/evgt-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable role-assignments module this unit runs.
terraform {
  source = "../../../modules/role-assignments"
}

# inputs: each value comes from an upstream unit's output — nothing hardcoded.
inputs = {
  principal_id            = dependency.function_app.outputs.principal_id
  key_vault_id            = dependency.key_vault.outputs.id
  servicebus_namespace_id = dependency.servicebus.outputs.id
  eventgrid_topic_id      = dependency.eventgrid.outputs.id
}
