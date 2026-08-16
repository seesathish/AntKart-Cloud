# =============================================================================
# Service Bus module — inputs
# =============================================================================
# This module defines HOW the Service Bus namespace and its entities (queue,
# topic, subscriptions) are built. The environment supplies WHAT values to use.
# No environment values baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the namespace is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Service Bus namespace (supplied from the Resource Group module's output)."
  type        = string
}

variable "namespace_name" {
  description = "Name of the Service Bus namespace. NOTE: namespace names are GLOBALLY UNIQUE (they become part of the <name>.servicebus.windows.net hostname)."
  type        = string
}

variable "queue_names" {
  description = "Queues to create (point-to-point / competing consumers — used for commands, which have a single owner)."
  type        = list(string)
  default     = ["order-commands"]
}

variable "topic_name" {
  description = "Topic to create (publish/subscribe — used for integration events, which have many listeners)."
  type        = string
  default     = "integration-events"
}

variable "subscription_names" {
  description = "Subscriptions on the topic — one per consuming service. Each subscription receives its own copy of every message published to the topic."
  type        = list(string)
  default     = ["products"]
}

variable "tags" {
  description = "Tags applied to the namespace, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
