# =============================================================================
# Event Grid module — inputs
# =============================================================================
# This module defines HOW the Event Grid custom topic is built. The environment
# supplies WHAT values to use. No environment values baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the topic is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Event Grid topic (supplied from the Resource Group module's output)."
  type        = string
}

variable "topic_name" {
  description = "Name of the Event Grid custom topic (the publish endpoint that producers send events to)."
  type        = string
}

variable "tags" {
  description = "Tags applied to the topic, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
