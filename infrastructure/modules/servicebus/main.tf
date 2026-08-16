# =============================================================================
# Service Bus module — namespace, queue, topic, subscriptions
# =============================================================================
# Provisions the reliable messaging backbone: a managed enterprise message
# broker for messages that MUST NOT be lost (commands and business data).
#
# Queue vs topic, in one line each:
#   * Queue  = point-to-point / competing consumers — exactly ONE consumer
#     processes each message. Used for COMMANDS, which have a single owner.
#   * Topic + subscriptions = publish/subscribe — EVERY subscription receives
#     its own copy of each message. Used for INTEGRATION EVENTS, which have
#     many independent listeners.
#
# This module establishes the core, IaC-managed entities. The application's
# messaging library may provision additional topology at runtime in the code
# phase; that builds on this backbone.

# --- Namespace ---------------------------------------------------------------
# The top-level Service Bus resource that contains the queues and topics.
resource "azurerm_servicebus_namespace" "this" {
  name                = var.namespace_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # Standard is required for TOPICS (Basic offers queues only). It is a flat
  # ~US$10/month and is the single largest cost of the current infrastructure.
  sku = "Standard"

  # local_auth_enabled = false disables SAS (shared access signature) keys, so
  # the data plane accepts ONLY Microsoft Entra (Azure AD) identities — no
  # connection-string secrets to store, leak, or rotate. Consistent with the
  # platform's secret-less model.
  local_auth_enabled = false

  # Reject anything older than TLS 1.2.
  minimum_tls_version = "1.2"

  tags = var.tags
}

# --- Queues (point-to-point) -------------------------------------------------
# One queue per name. Commands go here: a single consumer competes for and
# processes each message exactly once.
resource "azurerm_servicebus_queue" "this" {
  for_each = toset(var.queue_names)

  name         = each.value
  namespace_id = azurerm_servicebus_namespace.this.id

  # Delivery is AT-LEAST-ONCE: a message may occasionally be delivered more than
  # once (e.g. if a consumer crashes after processing but before acknowledging),
  # so consumers must be IDEMPOTENT. After max_delivery_count failed attempts the
  # message is moved to the queue's built-in dead-letter sub-queue (see below).
  max_delivery_count = 10
}

# --- Topic (publish/subscribe) -----------------------------------------------
# Integration events are published here; each subscription below gets its own
# copy, so adding a listener never affects the others.
resource "azurerm_servicebus_topic" "this" {
  name         = var.topic_name
  namespace_id = azurerm_servicebus_namespace.this.id
}

# --- Subscriptions (one per consuming service) -------------------------------
# Each subscription is an independent copy of the topic's message stream for one
# consumer.
resource "azurerm_servicebus_subscription" "this" {
  for_each = toset(var.subscription_names)

  name     = each.value
  topic_id = azurerm_servicebus_topic.this.id

  # After 10 failed delivery attempts the message is moved to this
  # subscription's DEAD-LETTER sub-queue rather than being dropped. Dead-lettered
  # messages can be inspected and resubmitted, so nothing is silently lost.
  max_delivery_count = 10
}
