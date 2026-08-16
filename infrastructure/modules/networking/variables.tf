# =============================================================================
# Networking module — inputs
# =============================================================================
# This module defines HOW the virtual network, its subnets, and their NSGs are
# built. The environment supplies WHAT values to use. No environment-specific
# values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the network is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the network (supplied from the Resource Group module's output)."
  type        = string
}

variable "vnet_name" {
  description = "Name of the virtual network (e.g. vnet-antkart-dev-eastus)."
  type        = string
}

variable "vnet_address_space" {
  description = "The VNet's address space in CIDR notation, as a list (e.g. [\"10.0.0.0/16\"]). Every subnet must fit inside this range."
  type        = list(string)
}

# Subnets are described as a MAP keyed by subnet name. Using a map lets the
# module loop with for_each, so adding or removing a subnet is a data change in
# the environment — no new resource code. Each entry carries its address
# prefix(es) plus a couple of optional flags with sensible defaults.
variable "subnets" {
  description = "Map of subnet name => settings. address_prefixes is required and must be non-overlapping ranges inside the VNet; service_endpoints and private_endpoint_network_policies are optional."
  type = map(object({
    address_prefixes = list(string)

    # service_endpoints: optionally route specific Azure services over the VNet
    # backbone from this subnet (e.g. ["Microsoft.KeyVault"]). Defaults to none.
    service_endpoints = optional(list(string), [])

    # private_endpoint_network_policies: must be "Disabled" on a subnet that
    # hosts private endpoints; "Enabled" (the default) everywhere else.
    private_endpoint_network_policies = optional(string, "Enabled")

    # allow_internet_ingress: when true, the subnet's NSG additionally allows
    # inbound HTTP (80) and HTTPS (443) from the Internet service tag (see main.tf).
    # Set this ONLY on the subnet(s) that host an internet-facing load balancer /
    # ingress controller — never on the private-endpoints subnet. Defaults to false
    # so the deny-by-default baseline is preserved everywhere unless opted in.
    allow_internet_ingress = optional(bool, false)
  }))
}

variable "tags" {
  description = "Tags applied to the network resources, consistent with the resource group. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
