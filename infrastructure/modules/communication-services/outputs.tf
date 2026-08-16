# =============================================================================
# Communication Services module — outputs
# =============================================================================
# What later steps (the email-sending application code, and the role-assignment
# step) need. The connection strings and access keys are deliberately NOT output:
# the application authenticates to the Communication Service with its Microsoft
# Entra MANAGED IDENTITY, not a key — so no secret appears in outputs or the repo.

output "communication_service_id" {
  description = "The Communication Service resource id (used to scope the data-plane role assignment granted to the app's managed identity)."
  value       = azurerm_communication_service.this.id
}

output "communication_service_hostname" {
  description = "The Communication Service endpoint host, e.g. acs-antkart-dev.communication.azure.com."
  value       = azurerm_communication_service.this.hostname
}

output "communication_service_endpoint" {
  description = "The full https endpoint the Email SDK connects to (with the managed-identity token credential)."
  value       = "https://${azurerm_communication_service.this.hostname}"
}

output "email_service_id" {
  description = "The Email Communication Service resource id."
  value       = azurerm_email_communication_service.this.id
}

output "sender_domain" {
  description = "The Azure-managed sender domain (the *.azurecomm.net subdomain shown to recipients in the From address)."
  value       = azurerm_email_communication_service_domain.this.from_sender_domain
}

output "sender_address" {
  description = "The MailFrom sender address the application sends as. Azure-managed domains auto-provision a DoNotReply mailbox, so this is DoNotReply@<managed-subdomain>."
  value       = "DoNotReply@${azurerm_email_communication_service_domain.this.from_sender_domain}"
}
