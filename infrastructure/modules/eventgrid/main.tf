# =============================================================================
# Event Grid module — custom topic
# =============================================================================
# Provisions the reactive, push-based eventing endpoint. Publishers send events
# to this topic; Event Grid then PUSHES them to subscriptions' handlers in
# near-real-time (contrast Service Bus, where consumers PULL durable work).
#
# This module creates the TOPIC only — the publish endpoint. Event SUBSCRIPTIONS
# are created later, once real handlers exist: a subscription needs a concrete
# destination, so wiring one now would point at nothing.

resource "azurerm_eventgrid_topic" "this" {
  name                = var.topic_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # local_auth_enabled = false disables key-based publishing. Publishers must
  # authenticate with their Microsoft Entra (Azure AD) identity instead of an
  # access key — consistent with the platform's secret-less model (nothing to
  # store, leak, or rotate).
  local_auth_enabled = false

  # input_schema is the shape Event Grid expects published events to arrive in.
  # "EventGridSchema" is Event Grid's native schema. "CloudEvents" (the open
  # CNCF standard) is the alternative — either works; the platform uses the
  # native Event Grid schema here.
  input_schema = "EventGridSchema"

  tags = var.tags
}
