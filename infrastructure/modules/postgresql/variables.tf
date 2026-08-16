# =============================================================================
# PostgreSQL Flexible Server module — inputs
# =============================================================================
# This module defines HOW the PostgreSQL Flexible Server and its databases are
# built. The environment supplies WHAT values to use. No environment values are
# baked in here.
#
# COST MODEL: one server hosts MANY databases. A Flexible Server is the billed
# unit (compute + storage); the databases inside it are free logical containers.
# The platform's four relational services therefore share a single small server
# rather than running four separate servers — one bill, not four.

variable "resource_group_name" {
  description = "Name of the resource group the server is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the PostgreSQL Flexible Server (supplied from the Resource Group module's output)."
  type        = string
}

variable "server_name" {
  description = "Name of the PostgreSQL Flexible Server. NOTE: server names are GLOBALLY UNIQUE — they become part of the <name>.postgres.database.azure.com hostname (lowercase letters, numbers, hyphens; 3-63 chars)."
  type        = string
}

variable "postgresql_version" {
  description = "Major PostgreSQL engine version the server runs."
  type        = string
  default     = "16"
}

variable "administrator_login" {
  description = "Administrator (superuser) login for the server. Must not be a reserved name (e.g. azure_superuser, admin, administrator, root, guest, public)."
  type        = string
  default     = "antkartadmin"
}

variable "sku_name" {
  description = "Compute SKU. B_Standard_B1ms is the smallest BURSTABLE tier (1 vCore, 2 GiB) — cheapest option that fits a low-traffic dev workload; it accrues CPU credits while idle and bursts when needed. Scale up to a General Purpose SKU for steady production load."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "storage_mb" {
  description = "Allocated storage in MB. 32768 (32 GiB) is the smallest reasonable size and ample for dev. Storage can be grown later but never shrunk."
  type        = number
  default     = 32768
}

variable "backup_retention_days" {
  description = "Number of days automated backups are retained (7-35). 7 is the minimum and is appropriate for dev."
  type        = number
  default     = 7
}

variable "database_names" {
  description = "Databases to create on the server — one per relational service. Each is a free logical container inside the single billed server."
  type        = list(string)
  default     = ["AKOrdersDb", "AKPaymentsDb", "AKNotificationsDb", "AKDiscountDb"]
}

variable "allowed_client_ip" {
  description = "A single public IPv4 address allowed through the server firewall — the developer's machine, so a local client (psql, EF migrations) can reach the server while public access is enabled. See the environment unit for the value."
  type        = string
}

variable "tags" {
  description = "Tags applied to the server, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
