# =============================================================================
# Terragrunt LIVE configuration — dev / Networking
# =============================================================================
# The deployable instance of the networking module for the dev environment.
# The module says HOW to build the VNet/subnets/NSGs; this file says WHAT values
# to use and wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: this unit needs the resource group created by the resource-group
# unit. Terragrunt applies that unit FIRST and exposes its outputs here, so the
# real resource group name and location are wired in — never hardcoded — and the
# apply order is enforced automatically.
dependency "resource_group" {
  config_path = "../resource-group"

  # mock_outputs let `init`/`plan`/`validate` run even before the dependency has
  # been applied (e.g. on a clean checkout or in CI), by supplying placeholder
  # values. A real `apply` always uses the dependency's actual outputs.
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable networking module this unit runs.
terraform {
  source = "../../../modules/networking"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs; the rest are dev's chosen network layout.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  vnet_name          = "vnet-antkart-dev-eastus"
  vnet_address_space = ["10.0.0.0/16"]

  # Subnets — non-overlapping ranges inside 10.0.0.0/16:
  #   aks               10.0.0.0/22  -> .0 .. .3   (1,024 addresses; the large one)
  #   private-endpoints 10.0.4.0/24  -> .4         (256 addresses)
  #   gateway           10.0.5.0/27  -> .5 (.0-.31)(32 addresses)
  subnets = {
    "aks" = {
      address_prefixes = ["10.0.0.0/22"]
      # The AKS nodes host the internet-facing ingress-nginx LoadBalancer, so this
      # subnet's NSG must permit inbound 80/443 from the Internet (the custom NSG
      # means AKS won't add those rules itself). Only this subnet opens them.
      allow_internet_ingress = true
    }
    "private-endpoints" = {
      address_prefixes = ["10.0.4.0/24"]
      # Private endpoints require network policies disabled on their subnet.
      private_endpoint_network_policies = "Disabled"
      # Stays closed to the internet (allow_internet_ingress defaults to false).
    }
    "gateway" = {
      address_prefixes = ["10.0.5.0/27"]
      # Stays closed to the internet (allow_internet_ingress defaults to false).
    }
  }

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
