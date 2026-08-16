# =============================================================================
# Role Assignments module — least-privilege data-plane grants
# =============================================================================
# This is where the secret-less model completes. Every resource was configured
# to accept ONLY Microsoft Entra identities (no keys / SAS). Here the consuming
# workload's managed identity is granted the specific DATA-plane roles it needs.
#
# Control plane vs data plane:
#   * CONTROL-plane roles (e.g. Contributor, Key Vault Administrator) manage the
#     RESOURCE itself — create/configure/delete it.
#   * DATA-plane roles (below) let an identity USE the data inside the resource —
#     read a secret, receive/send a message, publish an event.
# These are all DATA-plane, and each is scoped to the SPECIFIC resource, never
# the subscription — least privilege in both the role chosen and the scope.
#
# Each assignment sets principal_type = "ServicePrincipal" so the provider does
# not have to look the principal up — this avoids a transient "principal not
# found" error while a just-created managed identity replicates across Entra.

# --- Key Vault: read secrets (not manage them) -------------------------------
# "Key Vault Secrets User" can READ secret VALUES. It deliberately is NOT
# "Secrets Officer" (which can also create/delete/manage secrets) — the app only
# needs to read, so that is all it gets.
resource "azurerm_role_assignment" "kv_secrets_user" {
  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = var.principal_id
  principal_type       = "ServicePrincipal"
}

# --- Service Bus: receive messages -------------------------------------------
# "Azure Service Bus Data Receiver" can RECEIVE (consume) messages. Not Owner,
# not Manage — just receive, scoped to this namespace.
resource "azurerm_role_assignment" "sb_data_receiver" {
  scope                = var.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = var.principal_id
  principal_type       = "ServicePrincipal"
}

# --- Service Bus: send messages ----------------------------------------------
# "Azure Service Bus Data Sender" can SEND messages. Separate from Receiver so
# each capability is granted explicitly; still scoped to this namespace only.
resource "azurerm_role_assignment" "sb_data_sender" {
  scope                = var.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = var.principal_id
  principal_type       = "ServicePrincipal"
}

# --- Event Grid: publish events ----------------------------------------------
# "EventGrid Data Sender" can PUBLISH events to the topic. Not Contributor (which
# manages the topic) — just publish, scoped to this one topic.
resource "azurerm_role_assignment" "eventgrid_data_sender" {
  scope                = var.eventgrid_topic_id
  role_definition_name = "EventGrid Data Sender"
  principal_id         = var.principal_id
  principal_type       = "ServicePrincipal"
}

# NOTE: AcrPull for the AKS kubelet identity is NOT granted here — that is a
# DIFFERENT identity, created with the cluster, and is deferred to the AKS
# milestone.
