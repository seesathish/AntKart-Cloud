# =============================================================================
# Azure Managed Redis module — inputs
# =============================================================================
# This module defines HOW the Managed Redis cache is built. The environment
# supplies WHAT values to use. No environment values are baked in here.
#
# The cache backs the ShoppingCart service (cart state with a TTL). It is a
# single managed node by default — see main.tf for the Balanced_B0 rationale.

variable "resource_group_name" {
  description = "Name of the resource group the cache is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Managed Redis cache (supplied by the environment)."
  type        = string
}

variable "name" {
  description = "Name of the Managed Redis cache. NOTE: cache names are GLOBALLY UNIQUE — they become part of the <name>.<region>.redis.azure.net hostname (lowercase letters, numbers, hyphens)."
  type        = string
}

variable "sku_name" {
  description = "Managed Redis SKU. Balanced_B0 is the smallest/cheapest (~1 GB, single node) — ideal for dev/test. Larger Balanced_*, MemoryOptimized_*, or ComputeOptimized_* SKUs scale up for production."
  type        = string
  default     = "Balanced_B0"
}

variable "high_availability_enabled" {
  description = "Whether to run a replica for automatic failover. Disabled in dev/test to halve cost (single node, no SLA); PRODUCTION should enable HA."
  type        = bool
  default     = false
}

variable "clustering_policy" {
  description = "How the cache exposes itself to clients. 'EnterpriseCluster' presents a SINGLE proxied endpoint — most compatible with a standard StackExchange.Redis client, which needs no cluster awareness. ('OSSCluster' exposes the native Redis Cluster protocol.)"
  type        = string
  default     = "EnterpriseCluster"
}

variable "eviction_policy" {
  description = "What the cache evicts when memory is full. 'AllKeysLRU' evicts the least-recently-used key regardless of TTL — the cache-appropriate choice for cart data."
  type        = string
  default     = "AllKeysLRU"
}

variable "tags" {
  description = "Tags applied to the cache, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
