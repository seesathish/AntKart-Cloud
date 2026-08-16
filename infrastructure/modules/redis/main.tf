# =============================================================================
# Azure Managed Redis module — cache
# =============================================================================
# Provisions a managed Redis cache for the ShoppingCart service, which stores
# cart state keyed per user with a TTL.
#
# WHY azurerm_managed_redis (NOT azurerm_redis_cache): Azure has RETIRED the
# classic "Azure Cache for Redis" for new creation. The current offering is
# AZURE MANAGED REDIS (the Redis Enterprise-based service), provisioned via the
# azurerm_managed_redis resource. The connection model is the same from the
# application's point of view: hostname + access key over TLS.
#
# WHY Balanced_B0 + HA DISABLED: Balanced_B0 is the smallest/cheapest Managed
# Redis SKU (~1 GB, single node) — right for dev/test, where a brief cache
# outage just means carts are rebuilt, not lost business. high_availability is
# left off so there is no replica to pay for; PRODUCTION should enable HA for
# automatic failover and an SLA.
#
# ACCESS MODEL: connections are KEY-BASED over TLS. Managed Redis listens on TLS
# port 10000 (NOT the classic 6380); clients authenticate with an access key
# (see outputs.tf). The key is a secret retrieved and stored in Key Vault in a
# later step — never committed. TLS 1.2 is the floor: Managed Redis ENFORCES it
# by default, so there is no minimum_tls_version attribute on this resource
# (unlike the classic azurerm_redis_cache).
#
# M5 HARDENING (deferred): private networking via private endpoints is a later
# security-hardening step, not this dev provisioning.

resource "azurerm_managed_redis" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location

  # Balanced_B0: smallest/cheapest single-node SKU — see variables.tf.
  sku_name = var.sku_name

  # No replica in dev/test (cost); production should enable HA.
  high_availability_enabled = var.high_availability_enabled

  # default_database: the cache's data endpoint configuration.
  #   * EnterpriseCluster clustering presents a SINGLE proxied endpoint, so a
  #     standard StackExchange.Redis client connects without any cluster
  #     awareness.
  #   * AllKeysLRU eviction discards the least-recently-used key when memory is
  #     full — the right policy for a cache.
  default_database {
    clustering_policy = var.clustering_policy
    eviction_policy   = var.eviction_policy

    # access_keys_authentication_enabled = true is REQUIRED for the primary
    # access key to be exported (it is only generated when key auth is on). This
    # is the Option-1 key-based / vaulted model — the key is copied into Key
    # Vault and the service reads it from there. Entra-only auth (disabling
    # access keys) is the M5 hardening step.
    access_keys_authentication_enabled = true
  }

  tags = var.tags
}
