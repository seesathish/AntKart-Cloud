# =============================================================================
# Terragrunt LIVE configuration — dev / AKS
# =============================================================================
# The deployable instance of the aks module for the dev environment. The module
# says HOW to build the cluster; this file says WHAT values to use and wires in
# the upstream units it depends on. It COMPOSES four upstream outputs: the
# resource group (name/location), the networking VNet (the "aks" subnet id), the
# container registry (id, for the AcrPull grant), and observability (the Log
# Analytics workspace id, for monitoring).

# include "root": inherit the shared remote state backend (Azure AD auth) and the
# shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# --- Upstream dependencies ---------------------------------------------------
# One dependency per output this unit needs. Terragrunt applies each FIRST and
# passes its real outputs in. Each mock lets init/plan/validate run before the
# upstream units have been applied (e.g. on a clean checkout or in CI).

# The resource group the cluster is created in.
dependency "resource_group" {
  config_path = "../resource-group"
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The VNet — supplies subnet_ids, a map of subnet name => id. The cluster's nodes
# go in the "aks" subnet (10.0.0.0/22), the large subnet the networking unit
# sized specifically for the cluster.
dependency "networking" {
  config_path = "../networking"
  mock_outputs = {
    subnet_ids = {
      "aks"               = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.Network/virtualNetworks/vnet-antkart-dev-eastus/subnets/aks"
      "private-endpoints" = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.Network/virtualNetworks/vnet-antkart-dev-eastus/subnets/private-endpoints"
      "gateway"           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.Network/virtualNetworks/vnet-antkart-dev-eastus/subnets/gateway"
    }
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The container registry — its id scopes the kubelet identity's AcrPull grant.
dependency "container_registry" {
  config_path = "../container-registry"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.ContainerRegistry/registries/acrantkartdev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# Observability — supplies the Log Analytics workspace the OMS agent reports to.
dependency "observability" {
  config_path = "../observability"
  mock_outputs = {
    workspace_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.OperationalInsights/workspaces/log-antkart-dev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable aks module this unit runs.
terraform {
  source = "../../../modules/aks"
}

# inputs: this environment's values. Names/locations and every resource id come
# from the dependencies' outputs — nothing hardcoded except dev's own choices.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  cluster_name = "aks-antkart-dev"

  # The nodes go in the "aks" subnet — the large 10.0.0.0/22 subnet the
  # networking unit created for the cluster (subnet_ids is keyed by subnet name).
  subnet_id = dependency.networking.outputs.subnet_ids["aks"]

  # ACR id for the kubelet AcrPull grant; workspace id for the OMS agent.
  acr_id                     = dependency.container_registry.outputs.id
  log_analytics_workspace_id = dependency.observability.outputs.workspace_id

  # Free control plane + burstable B2s nodes keep dev cost low; auto-scaling is
  # left off in the module for predictable cost.
  sku_tier     = "Free"
  node_vm_size = "Standard_D2s_v3"
  node_count   = 2

  # local_account_disabled stays false in dev so we cannot lock ourselves out
  # before Entra RBAC access is verified.
  local_account_disabled = false

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }

  # AKS retires versions continually. Versions offered only under AKSLongTermSupport
  # require an LTS-tier subscription; pick one marked KubernetesOfficial.
  # Verify with: az aks get-versions --location eastus -o table
  kubernetes_version = "1.35"
}
