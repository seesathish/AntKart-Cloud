# =============================================================================
# Observability module — Log Analytics Workspace + Application Insights
# =============================================================================
# Provisions the DESTINATION for the platform's telemetry. The application
# instrumentation that produces the telemetry is wired later (in the code
# phase); this module just creates where it lands and how it is queried.
#
# Roles, kept distinct:
#   * Log Analytics Workspace — the central STORE. All telemetry lands here and
#     is queried with KQL (Kusto Query Language).
#   * Application Insights     — the APM layer that COLLECTS and VISUALIZES
#     application telemetry (requests, dependencies, exceptions, distributed
#     traces). Modern App Insights is "workspace-based": it stores its data in a
#     Log Analytics workspace rather than in its own classic store — which is
#     why the workspace is created first and App Insights points at it.

# --- Log Analytics Workspace -------------------------------------------------
# The central telemetry store. Created first so App Insights can target it.
resource "azurerm_log_analytics_workspace" "this" {
  name                = var.log_analytics_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # PerGB2018 is the standard pay-per-GB ingestion tier.
  sku = "PerGB2018"

  # Retention: how long telemetry is kept. Kept short for dev to keep cost low;
  # 30 days is the workspace minimum and is within the free retention window.
  retention_in_days = var.retention_days

  tags = var.tags
}

# --- Application Insights (workspace-based) ----------------------------------
# The APM layer. workspace_id makes this a WORKSPACE-BASED component: its data
# is stored in the Log Analytics workspace above, unifying application telemetry
# and platform logs in one queryable place.
resource "azurerm_application_insights" "this" {
  name                = var.app_insights_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # "web" is the application type for HTTP/API back-end services.
  application_type = "web"

  # Point App Insights at the workspace (workspace-based mode).
  workspace_id = azurerm_log_analytics_workspace.this.id

  tags = var.tags

  # NOTE: the application uses the outputs of this resource — the connection
  # string (and, legacy, the instrumentation key) — to send its telemetry here.
  # Those are exposed as sensitive outputs and consumed by app config later.
}
