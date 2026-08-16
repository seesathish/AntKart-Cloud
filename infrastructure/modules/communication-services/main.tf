# =============================================================================
# Communication Services module — ACS Email (managed sender domain)
# =============================================================================
# Provisions Azure Communication Services (ACS) Email: the managed service the
# platform uses to SEND transactional email (order confirmations, receipts,
# cancellation notices) without running or contracting a separate SMTP/email
# provider.
#
# ACS Email has THREE pieces plus a link, created here in order:
#
#   1. Email Communication Service  — the container that owns email DOMAINS.
#   2. Email domain (Azure-managed)  — the actual sender domain. With
#      domain_management = "AzureManaged", Azure provisions a FREE sender
#      subdomain under *.azurecomm.net and AUTO-CONFIGURES its DNS (SPF/DKIM)
#      for us — so there is NO custom domain to buy and NO DNS records to add.
#      (The alternative, "CustomerManaged", would require owning a domain and
#      publishing verification/DKIM/SPF records yourself.)
#   3. Communication Service         — the resource the application connects to;
#      it exposes the endpoint the Email SDK talks to.
#   4. Domain association            — links the managed domain (2) to the
#      Communication Service (3) as an allowed SENDER, so the service may send
#      mail "from" that domain.
#
# AUTHENTICATION — no keys. The Communication Service does expose connection
# strings / access keys, but the application authenticates with its Microsoft
# Entra MANAGED IDENTITY (Azure AD token) against the service endpoint instead.
# That identity is granted a data-plane role on the Communication Service in the
# role-assignment step — so no key or connection string is output here, stored,
# or committed, consistent with the platform's secret-less model.
#
# PROVIDER PIN — these resources come from the azurerm provider. The version
# constraint (~> 4.76) is declared ONCE in the shared root (environments/dev/
# root.hcl generates versions.tf into every unit), so this module does not — and
# must not — declare its own, which would collide with the generated file.

# --- 1. Email Communication Service ------------------------------------------
# The parent resource that holds email domains. data_location fixes where email
# metadata resides at rest (a data-residency choice, not a deployment region).
resource "azurerm_email_communication_service" "this" {
  name                = "acs-email-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  data_location       = var.data_location

  tags = var.tags
}

# --- 2. Email domain (Azure-managed) -----------------------------------------
# The sender domain. For an Azure-managed domain the resource name MUST be the
# literal "AzureManagedDomain" (the provider requires this when domain_management
# is "AzureManaged"). Azure issues a random *.azurecomm.net subdomain and wires
# up its SPF/DKIM automatically — zero DNS work on our side.
resource "azurerm_email_communication_service_domain" "this" {
  name              = "AzureManagedDomain"
  email_service_id  = azurerm_email_communication_service.this.id
  domain_management = "AzureManaged"

  tags = var.tags
}

# --- 3. Communication Service ------------------------------------------------
# The resource the application connects to. Its endpoint (the `hostname`
# attribute) is what the Email SDK targets; the app presents an Entra token, so
# no key is needed.
resource "azurerm_communication_service" "this" {
  name                = "acs-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  data_location       = var.data_location

  tags = var.tags
}

# --- 4. Domain association (sender link) --------------------------------------
# Authorises the Communication Service to send mail "from" the managed domain.
# Until this link exists, a send call would be rejected: the service has no
# verified sender. This is the piece that ties (2) and (3) together.
resource "azurerm_communication_service_email_domain_association" "this" {
  communication_service_id = azurerm_communication_service.this.id
  email_service_domain_id  = azurerm_email_communication_service_domain.this.id
}
