# =============================================================================
# AKS module — resources
# =============================================================================
# The managed Kubernetes cluster the platform's services run on. Built entirely
# from inputs, so the same module produces a structurally identical cluster in any
# environment. The cluster is created with the two features the later SECRET-LESS
# workload-identity work needs (OIDC issuer + workload identity) already switched
# on — enabling them at creation avoids a disruptive cluster update afterwards.

resource "azurerm_kubernetes_cluster" "this" {
  name                = var.cluster_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # dns_prefix forms the API server FQDN. Defaults to the cluster name.
  dns_prefix = coalesce(var.dns_prefix, var.cluster_name)

  # kubernetes_version pins the control-plane version for a reproducible plan.
  kubernetes_version = var.kubernetes_version

  # sku_tier: Free control plane for dev (no uptime SLA). A variable so production
  # can select Standard (financially-backed SLA) from the same module.
  sku_tier = var.sku_tier

  # local_account_disabled: keep the local admin kubeconfig available by default
  # so we cannot lock ourselves out before Entra RBAC is verified. Flip to true
  # (via the variable) once Entra access is confirmed.
  local_account_disabled = var.local_account_disabled

  # --- Secret-less foundations (REQUIRED at creation) -------------------------
  # oidc_issuer_enabled publishes the cluster's OIDC issuer; workload_identity_
  # enabled turns on the webhook that federates a Kubernetes ServiceAccount to an
  # Entra identity. Together they let pods obtain Entra tokens with NO secret.
  # Enabling both here (not later) avoids an in-place cluster update.
  oidc_issuer_enabled       = true
  workload_identity_enabled = true

  # --- Default (system) node pool --------------------------------------------
  default_node_pool {
    name           = "system"
    vm_size        = var.node_vm_size
    node_count     = var.node_count
    vnet_subnet_id = var.subnet_id

    # Auto-scaling is intentionally DISABLED for now to keep dev cost
    # predictable: the pool holds exactly node_count nodes. Enabling it later is
    # a node-pool update (min/max count), not a cluster rebuild.
    auto_scaling_enabled = false
  }

  # --- Identity ---------------------------------------------------------------
  # SystemAssigned: Azure manages the control-plane identity's lifecycle with the
  # cluster. The nodes get a SEPARATE, auto-created kubelet identity (used for
  # image pulls) — that is the one granted AcrPull below.
  identity {
    type = "SystemAssigned"
  }

  # --- Networking: Azure CNI Overlay -----------------------------------------
  # network_plugin "azure" + network_plugin_mode "overlay": pods get IPs from the
  # logical pod_cidr OVERLAY (below), not from the VNet, so the VNet's address
  # space is not consumed per-pod and the cluster scales without VNet IP pressure.
  # network_policy "azure": in-cluster network policy enforced by the Azure CNI.
  #
  # CIDR choice — none of these overlap the VNet (10.0.0.0/16) or each other:
  #   pod_cidr       10.244.0.0/16  overlay pod IPs (NAT'd to the node IP on egress)
  #   service_cidr   10.245.0.0/16  ClusterIP service range
  #   dns_service_ip 10.245.0.10    CoreDNS, inside service_cidr
  # The nodes themselves sit in the "aks" VNet subnet (var.subnet_id, 10.0.0.0/22).
  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_policy      = "azure"
    pod_cidr            = var.pod_cidr
    service_cidr        = var.service_cidr
    dns_service_ip      = var.dns_service_ip
  }

  # --- Access control: Entra (Azure AD) + Azure RBAC --------------------------
  # azure_rbac_enabled routes Kubernetes authorization through Azure RBAC, so
  # cluster access is granted with Azure role assignments on Entra identities —
  # consistent with the platform's secret-less model. admin_group_object_ids
  # (optional) grants listed Entra groups cluster-admin.
  azure_active_directory_role_based_access_control {
    azure_rbac_enabled     = true
    admin_group_object_ids = var.admin_group_object_ids
  }

  # --- Monitoring (optional) --------------------------------------------------
  # Wire the OMS agent to the existing Log Analytics workspace when one is
  # supplied (the dev unit passes the observability module's workspace_id). When
  # null, the block is omitted and no monitoring add-on is enabled.
  dynamic "oms_agent" {
    for_each = var.log_analytics_workspace_id == null ? [] : [var.log_analytics_workspace_id]
    content {
      log_analytics_workspace_id = oms_agent.value
    }
  }

  tags = var.tags
}

# =============================================================================
# AcrPull for the cluster's kubelet identity
# =============================================================================
# Grants the NODES' (kubelet) identity the built-in AcrPull role on the existing
# registry, so image pulls need NO registry credentials — the secret-less model.
#
# WHY this lives in the AKS module rather than the role-assignments module:
# the repo's role-assignments unit is a purpose-built COMPOSITION wired to the
# Function App's identity (fixed principal + Key Vault/Service Bus/Event Grid
# scopes); it is not a generic role grantor. Both container-registry/main.tf and
# role-assignments/main.tf explicitly defer this grant to "the AKS wiring" — the
# kubelet identity does not even exist until the cluster is created here. Keeping
# the assignment beside the cluster that produces the identity matches that
# established decision and keeps the grant co-located with its principal.
resource "azurerm_role_assignment" "kubelet_acr_pull" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id

  # principal_type set explicitly so the provider does not look the principal up —
  # avoids a transient "principal not found" while the just-created kubelet
  # identity replicates across Entra (same rationale as the role-assignments unit).
  principal_type = "ServicePrincipal"
}
