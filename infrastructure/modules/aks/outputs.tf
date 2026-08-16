# =============================================================================
# AKS module — outputs
# =============================================================================
# Later steps consume these: deploy tooling needs the cluster name/id; the
# node_resource_group is where AKS creates the node/VMSS resources; the kubelet
# identity object id is used when granting the nodes access to other resources;
# the oidc_issuer_url is REQUIRED to configure federated workload-identity
# credentials; kube_config is the admin credential to reach the API server.

output "name" {
  description = "The managed cluster name."
  value       = azurerm_kubernetes_cluster.this.name
}

output "id" {
  description = "The managed cluster resource id."
  value       = azurerm_kubernetes_cluster.this.id
}

output "node_resource_group" {
  description = "The auto-created resource group (MC_...) that holds the cluster's node/VMSS/load-balancer resources."
  value       = azurerm_kubernetes_cluster.this.node_resource_group
}

output "kubelet_identity_object_id" {
  description = "Object id of the nodes' kubelet identity (granted AcrPull here; reused to grant the nodes access to other resources later)."
  value       = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

output "oidc_issuer_url" {
  description = "The cluster's OIDC issuer URL — required to configure federated identity credentials for workload identity."
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
}

# kube_config is the admin kubeconfig for the cluster's API server. It is a
# credential and is marked sensitive so Terraform never prints it in plan/apply
# output or logs; it is consumed via state, never committed.
output "kube_config" {
  description = "Raw admin kubeconfig for the cluster (sensitive credential)."
  value       = azurerm_kubernetes_cluster.this.kube_config_raw
  sensitive   = true
}
