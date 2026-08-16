# =============================================================================
# Function App module — Consumption plan + runtime storage + Function App shell
# =============================================================================
# Provisions the HOME the notification function deploys into later. Three pieces:
#   1. a Consumption (serverless) hosting plan,
#   2. a storage account dedicated to the Functions RUNTIME's internal plumbing,
#   3. the Function App shell, wired to Application Insights.
# The function CODE is deployed in a later phase; this step just creates where
# it lands.

# --- 1. Consumption hosting plan --------------------------------------------
# sku_name "Y1" is the Consumption plan: pay-per-execution, scale-to-zero
# serverless hosting. Linux os_type to match the .NET isolated worker below.
resource "azurerm_service_plan" "this" {
  name                = "plan-${var.function_app_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = "Y1"
  tags                = var.tags
}

# --- 2. Runtime storage account ---------------------------------------------
# The Functions runtime REQUIRES a storage account for its OWN internal
# plumbing: trigger state, execution leases, host metadata, etc. This is NOT
# application data — it is the platform's bookkeeping. Hence a dedicated account.
resource "azurerm_storage_account" "this" {
  name                = var.storage_account_name
  resource_group_name = var.resource_group_name
  location            = var.location

  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false

  # DOCUMENTED EXCEPTION to the platform's Entra-only model: on the Consumption
  # plan the Functions runtime connects to this account via its SHARED KEY
  # (the AzureWebJobsStorage setting below). Shared-key access therefore stays
  # ENABLED here. This exception is deliberate and NARROWLY SCOPED — it applies
  # only to the runtime's internal plumbing, never to application data or to
  # other services. Identity-based storage connections are the production
  # hardening path; application-level access to other services stays
  # identity-based.
  tags = var.tags
}

# --- 3. Function App shell ---------------------------------------------------
resource "azurerm_linux_function_app" "this" {
  name                = var.function_app_name
  resource_group_name = var.resource_group_name
  location            = var.location
  service_plan_id     = azurerm_service_plan.this.id

  # The runtime's storage connection (AzureWebJobsStorage). Shared key — the
  # documented exception explained above.
  storage_account_name       = azurerm_storage_account.this.name
  storage_account_access_key = azurerm_storage_account.this.primary_access_key

  # Reject plain HTTP.
  https_only = true

  site_config {
    application_stack {
      # .NET isolated worker on .NET 9: the function runs in its own process,
      # decoupled from the host runtime version (the forward-looking model).
      dotnet_version              = "9.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = {
    # Telemetry from day one: the Function App reports to Application Insights
    # using the connection string supplied by the observability unit.
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = var.app_insights_connection_string
  }

  # A system-assigned managed identity: an Azure-managed robot identity for this
  # Function App, with no secret to handle. It receives the data-plane roles it
  # needs (e.g. Service Bus receive, Key Vault secrets read) in a LATER step —
  # not here. Application-level access to other services is identity-based.
  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}
