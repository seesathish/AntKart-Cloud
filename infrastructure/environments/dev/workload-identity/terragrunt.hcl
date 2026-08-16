# =============================================================================
# Terragrunt LIVE configuration — dev / Workload Identity
# =============================================================================
# The deployable instance of the workload-identity module for the dev
# environment. The module says HOW to build per-service federated identities;
# this file says WHICH services exist, WHICH ServiceAccount each federates to, and
# the exact least-privilege roles each one gets — wired from the upstream units'
# real resource ids.
#
# Scope note — why cosmosdb / postgresql / redis are NOT dependencies here:
# those data stores are reached at runtime via a CONNECTION STRING stored in Key
# Vault (Cosmos: ProductsCosmosConnectionString; Postgres/Redis: the per-service
# connection-string secrets), not via an Azure data-plane RBAC role. So a service
# gets its database access transitively through "Key Vault Secrets User" on the
# vault — there is no Cosmos/Postgres/Redis role to grant, and depending on those
# units would wire outputs this unit never uses. Only Key Vault, Service Bus, and
# Event Grid expose data-plane RBAC that identities are granted directly.

# include "root": inherit the shared remote state backend (Azure AD auth) and the
# shared azurerm provider from environments/dev/root.hcl. The root also supplies
# the environment = "dev" input the module uses in identity names.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# --- Upstream dependencies ---------------------------------------------------
# One dependency per output this unit consumes. Terragrunt applies each FIRST and
# passes its real outputs in. Each mock lets init/plan/validate run before the
# upstream units have been applied (e.g. on a clean checkout or in CI).

# The resource group the identities are created in.
dependency "resource_group" {
  config_path = "../resource-group"
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The AKS cluster — supplies the OIDC issuer URL the federated credentials trust.
dependency "aks" {
  config_path = "../aks"
  mock_outputs = {
    oidc_issuer_url = "https://eastus.oic.prod-aks.azure.com/00000000-0000-0000-0000-000000000000/11111111-1111-1111-1111-111111111111/"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# Key Vault — every service's identity is granted "Key Vault Secrets User" here,
# which is also how each reaches its database (connection string held in the vault).
dependency "key_vault" {
  config_path = "../key-vault"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.KeyVault/vaults/kv-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# Service Bus namespace — scopes the Data Sender/Receiver grants for the services
# that run the MassTransit transport (Products, ShoppingCart, Order, Payments).
dependency "servicebus" {
  config_path = "../servicebus"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.ServiceBus/namespaces/sb-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# Event Grid topic — scopes the Data Sender grant for the services that publish
# notification side-effects (Order, Payments).
dependency "eventgrid" {
  config_path = "../eventgrid"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.EventGrid/topics/evgt-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable workload-identity module this unit runs.
terraform {
  source = "../../../modules/workload-identity"
}

# inputs: the service → ServiceAccount → least-privilege-roles matrix. Every role
# name is a real built-in Azure role already used elsewhere in the repo; every
# scope is an upstream unit's real resource id. Nothing is hardcoded or invented.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  oidc_issuer_url = dependency.aks.outputs.oidc_issuer_url

  # Pods run in the "antkart" namespace; ServiceAccounts follow the in-cluster
  # ak-<service> naming convention.
  namespace = "antkart"

  services = {
    # Products — catalogue on Cosmos (connection string from Key Vault) + Service
    # Bus (ReserveStockConsumer receives; publishes StockReserved/Failed → sends).
    products = {
      service_account_name = "ak-products"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
        { role_definition_name = "Azure Service Bus Data Sender", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "Azure Service Bus Data Receiver", scope = dependency.servicebus.outputs.id },
      ]
    }

    # ShoppingCart — cart in Redis (connection string from Key Vault) + Service
    # Bus (ClearCartOnOrderConfirmedConsumer receives; publishes → sends).
    cart = {
      service_account_name = "ak-cart"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
        { role_definition_name = "Azure Service Bus Data Sender", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "Azure Service Bus Data Receiver", scope = dependency.servicebus.outputs.id },
      ]
    }

    # Order — Postgres (connection string from Key Vault) + the saga on Service Bus
    # (sends and receives) + publishes order notifications to Event Grid.
    order = {
      service_account_name = "ak-order"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
        { role_definition_name = "Azure Service Bus Data Sender", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "Azure Service Bus Data Receiver", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "EventGrid Data Sender", scope = dependency.eventgrid.outputs.id },
      ]
    }

    # Payments — Postgres + Razorpay keys (both from Key Vault) + Service Bus
    # (consumes OrderConfirmed; publishes payment events → sends/receives) +
    # publishes payment notifications to Event Grid.
    payments = {
      service_account_name = "ak-payments"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
        { role_definition_name = "Azure Service Bus Data Sender", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "Azure Service Bus Data Receiver", scope = dependency.servicebus.outputs.id },
        { role_definition_name = "EventGrid Data Sender", scope = dependency.eventgrid.outputs.id },
      ]
    }

    # Discount — gRPC over Postgres only (connection string from Key Vault); no
    # messaging, so Key Vault Secrets User is all it needs.
    discount = {
      service_account_name = "ak-discount"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
      ]
    }

    # Gateway — routes and validates JWTs (Entra settings are non-secret). Granted
    # the Key Vault Secrets User baseline so it can read a vaulted value if wired later.
    gateway = {
      service_account_name = "ak-gateway"
      role_assignments = [
        { role_definition_name = "Key Vault Secrets User", scope = dependency.key_vault.outputs.id },
      ]
    }
  }

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
