# =============================================================================
# PostgreSQL Flexible Server module — server, databases, firewall
# =============================================================================
# Provisions a single managed PostgreSQL Flexible Server that hosts ALL of the
# platform's relational databases (Orders, Payments, Notifications, Discount).
#
# WHY ONE SERVER, MANY DATABASES: the server is the billed unit (compute +
# storage); databases inside it cost nothing extra. Four services on one small
# Burstable server is dramatically cheaper than four servers, and is the right
# shape for dev where total load is tiny. Logical isolation between services is
# preserved — each owns its own database.
#
# See docs/guides/ for the relational-data concepts.

# --- Administrator password --------------------------------------------------
# The admin password is GENERATED, never written in source. It is held only in
# Terraform state (which lives in the encrypted, Entra-auth-only state storage)
# and surfaced through a sensitive output so a later step can copy it into Key
# Vault. random_password has no keepers, so it is generated once and stays
# stable across applies (it is not regenerated on every run).
resource "random_password" "admin" {
  length  = 24
  special = true
  # Restrict the special characters to a connection-string-safe set (no @, /, :,
  # quotes, spaces or backslashes), so the value can be dropped into an ADO.NET /
  # libpq connection string or a URL without escaping surprises.
  override_special = "!#%*-_=+"
}

# --- Flexible Server ---------------------------------------------------------
resource "azurerm_postgresql_flexible_server" "this" {
  name                = var.server_name
  resource_group_name = var.resource_group_name
  location            = var.location

  version = var.postgresql_version

  administrator_login    = var.administrator_login
  administrator_password = random_password.admin.result

  # Burstable B1ms: the smallest/cheapest tier — see variables.tf for rationale.
  sku_name   = var.sku_name
  storage_mb = var.storage_mb

  backup_retention_days = var.backup_retention_days

  # geo_redundant_backup_enabled = false keeps backups single-region (no paired-
  # region copy) — cheaper, and sufficient for dev.
  geo_redundant_backup_enabled = false

  # No high_availability block ⇒ zone-redundant HA is DISABLED. Zone-redundant HA
  # runs a hot standby in another availability zone (roughly doubling cost) and
  # is unnecessary for dev. It would be enabled for production.

  # public_network_access_enabled = true exposes a public endpoint, gated by the
  # firewall rules below. This lets a developer connect from their machine now,
  # before any virtual network / private DNS exists.
  # M5 HARDENING (deferred): move to PRIVATE access — inject the server into a
  # delegated subnet with a Private DNS zone (delegated_subnet_id /
  # private_dns_zone_id), drop the public endpoint, and remove the firewall
  # rules. That is a networking change, intentionally out of scope here.
  public_network_access_enabled = true

  tags = var.tags

  lifecycle {
    # prevent_destroy guards this stateful data resource: the server holds every
    # relational database, so an accidental destroy would lose all of them.
    # Terraform will refuse any plan that would delete the server until this
    # guard is intentionally removed.
    prevent_destroy = true
  }
}

# --- Databases (one per relational service) ----------------------------------
# Each database is a free logical container inside the single billed server.
# UTF8 with the default collation matches what the EF Core migrations expect.
resource "azurerm_postgresql_flexible_server_database" "this" {
  for_each = toset(var.database_names)

  name      = each.value
  server_id = azurerm_postgresql_flexible_server.this.id

  charset   = "UTF8"
  collation = "en_US.utf8"

  lifecycle {
    # Changing charset/collation forces the database (and its data) to be
    # recreated; guard against an accidental destructive change.
    prevent_destroy = true
  }
}

# --- Firewall: developer client IP -------------------------------------------
# With the public endpoint enabled, the server denies ALL traffic by default;
# every allowed source must be an explicit firewall rule. This rule opens the
# server to the developer's single public IP so a local client (psql, EF Core
# migrations) can reach it during development. It is a dev convenience that the
# M5 private-networking step removes.
resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_dev_ip" {
  name             = "allow-developer-ip"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = var.allowed_client_ip
  end_ip_address   = var.allowed_client_ip
}

# --- Firewall: allow Azure services ------------------------------------------
# The 0.0.0.0 -> 0.0.0.0 range is a SPECIAL rule meaning "allow access from
# Azure-internal services" (not the whole internet). It lets other Azure
# resources (e.g. the Function App / future AKS workloads) reach the server over
# the public endpoint without each one needing its own egress IP pinned here.
resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure_services" {
  name             = "allow-azure-services"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}
