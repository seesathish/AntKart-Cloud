# =============================================================================
# PostgreSQL Flexible Server module — outputs
# =============================================================================
# These feed the Key Vault + service-wiring step. The admin password is the only
# secret here; it is marked sensitive so Terraform never prints it. It is NOT
# stored in the repo — a later step copies it (and the per-service connection
# strings derived from it) into Key Vault, from which the services read at
# runtime.

output "server_name" {
  description = "The PostgreSQL Flexible Server name."
  value       = azurerm_postgresql_flexible_server.this.name
}

output "fqdn" {
  description = "The fully-qualified domain name (<name>.postgres.database.azure.com) clients and services connect to."
  value       = azurerm_postgresql_flexible_server.this.fqdn
}

output "administrator_login" {
  description = "The administrator (superuser) login configured on the server."
  value       = azurerm_postgresql_flexible_server.this.administrator_login
}

output "administrator_password" {
  description = "The generated administrator password. Sensitive — consumed only to seed Key Vault in a later step; never commit or print it."
  value       = random_password.admin.result
  sensitive   = true
}

output "database_names" {
  description = "The names of the databases created on the server (one per relational service)."
  value       = [for db in azurerm_postgresql_flexible_server_database.this : db.name]
}
