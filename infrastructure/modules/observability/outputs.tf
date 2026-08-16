# =============================================================================
# Observability module — outputs
# =============================================================================
# Later steps and application configuration consume these. The connection string
# is what the application uses to send telemetry to Application Insights.

output "workspace_id" {
  description = "The Log Analytics workspace resource id (e.g. for diagnostic settings on other resources)."
  value       = azurerm_log_analytics_workspace.this.id
}

output "workspace_name" {
  description = "The Log Analytics workspace name."
  value       = azurerm_log_analytics_workspace.this.name
}

output "app_insights_id" {
  description = "The Application Insights component resource id."
  value       = azurerm_application_insights.this.id
}

# The connection string is how an application points its telemetry at this App
# Insights instance. It is sensitive and is supplied to apps via config/secret
# store later, never committed.
output "connection_string" {
  description = "Application Insights connection string used by the application to send telemetry."
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
}

# The instrumentation key is the legacy equivalent of the connection string;
# kept available for compatibility. Connection string is preferred.
output "instrumentation_key" {
  description = "Application Insights instrumentation key (legacy; connection string is preferred)."
  value       = azurerm_application_insights.this.instrumentation_key
  sensitive   = true
}
