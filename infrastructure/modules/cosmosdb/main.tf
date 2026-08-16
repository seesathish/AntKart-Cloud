# =============================================================================
# Cosmos DB module — account (serverless, MongoDB API) + database
# =============================================================================
# Provisions a fully-managed document database for the product catalog. This
# module creates the ACCOUNT and the DATABASE only; collections (and their
# partition keys) are created by the application/seeder at runtime, because the
# partition-key choice is a data-modelling decision made in the data-migration
# phase, not infrastructure.
#
# See docs/guides/cosmosdb-concepts.md for the concepts (RUs, serverless,
# MongoDB API, partition keys).

resource "azurerm_cosmosdb_account" "this" {
  name                = var.account_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # offer_type is always "Standard" for Cosmos DB accounts.
  offer_type = "Standard"

  # kind = "MongoDB": the account speaks the MongoDB wire protocol, so existing
  # MongoDB driver code works by changing only the connection string.
  kind = "MongoDB"

  # The MongoDB server version the account presents to drivers.
  mongo_server_version = var.mongo_server_version

  # --- Serverless capacity mode ----------------------------------------------
  # EnableServerless bills per request (per RU consumed) with near-zero idle
  # cost — the right model for a spiky, mostly-idle dev workload. Steady
  # production traffic would instead use provisioned throughput. A serverless
  # account is single-region (one geo_location, failover_priority 0).
  capabilities {
    name = "EnableServerless"
  }

  # Azure implicitly registers EnableMongo on MongoDB-kind accounts server-side.
  # Declaring it here keeps the configuration aligned with the account's actual
  # state, so Terraform doesn't plan a destructive replacement to "remove" a
  # capability it never added (which prevent_destroy would block).
  capabilities {
    name = "EnableMongo"
  }

  # --- Consistency -----------------------------------------------------------
  # Session is Cosmos's default and the usual choice: within a client session
  # you always read your own writes, while avoiding the latency/cost of the
  # strongest levels. It balances consistency against performance.
  consistency_policy {
    consistency_level = "Session"
  }

  # Single write region for the serverless account.
  geo_location {
    location          = var.location
    failover_priority = 0
  }

  tags = var.tags

  lifecycle {
    # prevent_destroy guards this stateful data resource: the account holds the
    # product catalog data, so an accidental destroy would lose it. Terraform
    # will refuse any plan that would delete the account until this guard is
    # intentionally removed.
    prevent_destroy = true
  }
}

# --- MongoDB database --------------------------------------------------------
# The database namespace that the application's collections live in. No
# throughput is set: a serverless account does not provision RU/s — capacity is
# pay-per-request. Collections are created by the app/seeder at runtime.
resource "azurerm_cosmosdb_mongo_database" "this" {
  name                = var.database_name
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
}
