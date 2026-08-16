# =============================================================================
# Event Grid module — outputs
# =============================================================================
# Later steps reference these. The access keys are deliberately NOT output:
# publishing is done with Microsoft Entra identities granted the **EventGrid
# Data Sender** role on this topic in the managed-identity step — never an
# access key, so no secret appears in outputs or the repo.

output "id" {
  description = "The Event Grid topic resource id (used to scope the EventGrid Data Sender role assignment)."
  value       = azurerm_eventgrid_topic.this.id
}

output "name" {
  description = "The Event Grid topic name."
  value       = azurerm_eventgrid_topic.this.name
}

output "endpoint" {
  description = "The topic's publish endpoint URL that producers send events to."
  value       = azurerm_eventgrid_topic.this.endpoint
}
