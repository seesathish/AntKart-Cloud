# =============================================================================
# Cosmos DB module — outputs
# =============================================================================
# Later steps reference these. The connection string and account keys are
# deliberately NOT output here: they are sensitive secrets. They are retrieved
# and stored in Key Vault during the secrets / data-migration step, and the
# application reads them from there at runtime — never from the repo.

output "id" {
  description = "The Cosmos DB account resource id."
  value       = azurerm_cosmosdb_account.this.id
}

output "name" {
  description = "The Cosmos DB account name."
  value       = azurerm_cosmosdb_account.this.name
}

output "endpoint" {
  description = "The Cosmos DB account endpoint hostname."
  value       = azurerm_cosmosdb_account.this.endpoint
}

output "database_name" {
  description = "The MongoDB database name created in the account."
  value       = azurerm_cosmosdb_mongo_database.this.name
}
