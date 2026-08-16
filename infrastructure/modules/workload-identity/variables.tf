# =============================================================================
# Workload Identity module — inputs
# =============================================================================
# This module defines HOW per-service workload identities are built: for each
# service it creates a user-assigned managed identity, federates it to the AKS
# cluster's OIDC issuer (so a pod running under the matching Kubernetes
# ServiceAccount can obtain Entra tokens with NO stored secret), and grants that
# identity ONLY the data-plane roles the service actually needs (least privilege).
# The environment supplies WHAT services and WHAT roles; the module bakes in none.

variable "resource_group_name" {
  description = "Name of the resource group the identities are created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the identities (supplied from the Resource Group module's output)."
  type        = string
}

variable "environment" {
  description = "Environment name (e.g. dev), inherited from the root inputs. Used in resource names — identities are named id-ak-<service>-<environment>."
  type        = string
}

variable "oidc_issuer_url" {
  description = "The AKS cluster's OIDC issuer URL (from the aks module's oidc_issuer_url output). The federated credentials trust this issuer."
  type        = string
}

variable "namespace" {
  description = "Kubernetes namespace the service accounts live in. The federated-credential subject is system:serviceaccount:<namespace>:<service_account_name>."
  type        = string
  default     = "antkart"
}

# The set of services to create an identity for. Each entry names the Kubernetes
# ServiceAccount the identity federates to, and the exact role assignments it
# should receive — role_definition_name + scope — so least privilege is expressed
# per service in the live unit, and the module stays free of any resource-type or
# role-name knowledge.
variable "services" {
  description = "Map of service key (short name, e.g. products) => its ServiceAccount name and the least-privilege role assignments to grant its identity."
  type = map(object({
    service_account_name = string
    role_assignments = list(object({
      role_definition_name = string
      scope                = string
    }))
  }))
}

variable "tags" {
  description = "Tags applied to the identities, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
