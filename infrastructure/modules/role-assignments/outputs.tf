# =============================================================================
# Role Assignments module — outputs
# =============================================================================
# The role assignment ids, exposed for reference and audit.

output "key_vault_secrets_user_id" {
  description = "Id of the Key Vault Secrets User role assignment."
  value       = azurerm_role_assignment.kv_secrets_user.id
}

output "servicebus_data_receiver_id" {
  description = "Id of the Azure Service Bus Data Receiver role assignment."
  value       = azurerm_role_assignment.sb_data_receiver.id
}

output "servicebus_data_sender_id" {
  description = "Id of the Azure Service Bus Data Sender role assignment."
  value       = azurerm_role_assignment.sb_data_sender.id
}

output "eventgrid_data_sender_id" {
  description = "Id of the EventGrid Data Sender role assignment."
  value       = azurerm_role_assignment.eventgrid_data_sender.id
}
