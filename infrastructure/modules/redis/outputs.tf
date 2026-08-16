# =============================================================================
# Azure Managed Redis module — outputs
# =============================================================================
# These feed the Key Vault + service-wiring step. The access key and the
# connection string are secrets, marked sensitive so Terraform never prints
# them. They are NOT stored in the repo — a later step copies the connection
# string into Key Vault, from which the ShoppingCart service reads at runtime.

output "hostname" {
  description = "The Managed Redis hostname (<name>.<region>.redis.azure.net) clients connect to."
  value       = azurerm_managed_redis.this.hostname
}

output "ssl_port" {
  description = "The TLS port for client connections. Managed Redis uses 10000 (the classic 6380 does not apply); the resource exposes no separate port attribute, so it is fixed here."
  value       = 10000
}

output "primary_access_key" {
  description = "The primary access key used to authenticate to the cache. Sensitive — consumed only to seed Key Vault in a later step; never commit or print it."
  value       = azurerm_managed_redis.this.default_database[0].primary_access_key
  sensitive   = true
}

output "connection_string" {
  description = "Ready-to-use StackExchange.Redis connection string (TLS, key auth). Sensitive — copied into Key Vault in a later step; never commit or print it."
  value       = "${azurerm_managed_redis.this.hostname}:10000,password=${azurerm_managed_redis.this.default_database[0].primary_access_key},ssl=True,abortConnect=False"
  sensitive   = true
}
