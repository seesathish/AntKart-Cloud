# =============================================================================
# AKS module — inputs
# =============================================================================
# This module defines HOW the managed Kubernetes cluster is built. The
# environment supplies WHAT values to use (names, sizes, the subnet/ACR/workspace
# ids from upstream units). No environment-specific values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the cluster is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the cluster (supplied from the Resource Group module's output)."
  type        = string
}

variable "cluster_name" {
  description = "Name of the managed cluster (e.g. aks-antkart-dev)."
  type        = string
}

variable "dns_prefix" {
  description = "DNS prefix for the cluster's API server FQDN. When null it defaults to the cluster name. Must start alphanumeric; alphanumeric and hyphens only."
  type        = string
  default     = null
}

variable "kubernetes_version" {
  description = "Control-plane Kubernetes version. Pinned so a plan is reproducible. A minor alias (e.g. \"1.31\") resolves to the latest supported patch; set an exact patch (e.g. \"1.31.2\") to avoid version drift on the control plane."
  type        = string
  default     = "1.31"
}

variable "sku_tier" {
  description = "Control-plane SKU tier: Free (no uptime SLA, fine for dev) or Standard (financially-backed SLA, for production). Kept a variable so the same module serves both."
  type        = string
  default     = "Free"
}

# --- Default (system) node pool ----------------------------------------------
variable "node_vm_size" {
  description = "VM size for the default node pool. Standard_B2s (2 vCPU / 4 GiB, burstable) keeps dev cost low; production overrides with a non-burstable size."
  type        = string
  default     = "Standard_B2s"
}

variable "node_count" {
  description = "Fixed number of nodes in the default node pool. Auto-scaling is intentionally disabled (see main.tf) so cost is predictable; this count is authoritative."
  type        = number
  default     = 2
}

variable "subnet_id" {
  description = "Resource id of the subnet the node pool's nodes are placed in (the \"aks\" subnet from the networking module's subnet_ids map)."
  type        = string
}

# --- Cluster networking (Azure CNI Overlay) ----------------------------------
# The pod and service CIDRs below are LOGICAL ranges internal to the cluster.
# With Azure CNI *Overlay*, pod IPs come from this overlay space and are NAT'd to
# the node IP leaving the node — they do NOT consume VNet address space, so these
# ranges must NOT overlap the VNet (10.0.0.0/16) or each other. See main.tf.
variable "pod_cidr" {
  description = "Overlay CIDR pods draw IPs from. Must not overlap the VNet or the service CIDR."
  type        = string
  default     = "10.244.0.0/16"
}

variable "service_cidr" {
  description = "CIDR for Kubernetes ClusterIP services. Must not overlap the VNet or the pod CIDR."
  type        = string
  default     = "10.245.0.0/16"
}

variable "dns_service_ip" {
  description = "IP of the in-cluster DNS (CoreDNS) service. Must fall INSIDE service_cidr."
  type        = string
  default     = "10.245.0.10"
}

# --- Identity / access control -----------------------------------------------
variable "local_account_disabled" {
  description = "Disable the cluster's local (non-Entra) admin kubeconfig. Left configurable and defaulting to FALSE so we cannot lock ourselves out before Entra RBAC and group assignments are verified; flip to true once Entra access is confirmed."
  type        = bool
  default     = false
}

variable "admin_group_object_ids" {
  description = "Entra group object ids granted cluster-admin via AAD RBAC. Optional; empty by default (grants are then made per-user/group via Azure RBAC role assignments)."
  type        = list(string)
  default     = []
}

# --- Monitoring (optional) ----------------------------------------------------
variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace id for the OMS/monitoring agent. Optional: when null the OMS agent is not enabled. The dev unit wires this from the observability module's workspace_id output."
  type        = string
  default     = null
}

# --- ACR integration ----------------------------------------------------------
variable "acr_id" {
  description = "Resource id of the Azure Container Registry the cluster's kubelet identity is granted AcrPull on, so nodes pull images with no registry credentials."
  type        = string
}

variable "tags" {
  description = "Tags applied to the cluster, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
