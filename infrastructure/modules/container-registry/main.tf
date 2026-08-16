# =============================================================================
# Container Registry module — resource
# =============================================================================
# A private Azure Container Registry (ACR): the repository that holds the
# platform's container images. Built entirely from inputs, so the same module
# produces a structurally identical registry in any environment.

resource "azurerm_container_registry" "this" {
  name                = var.acr_name
  resource_group_name = var.resource_group_name
  location            = var.location

  # sku: the pricing tier. Basic for dev; Premium for production (it adds
  # private endpoints, geo-replication, and content trust). The tier is an
  # input so the same module serves both.
  sku = var.sku

  # admin_enabled = false: the built-in admin account is a single static
  # username/password for the whole registry. Disabling it forces all access to
  # go through Azure AD (Microsoft Entra) identities — managed identities and
  # RBAC roles — which is consistent with the platform's secret-less security
  # model: no shared credential to leak, rotate, or commit.
  admin_enabled = false

  tags = var.tags

  # NOTE: pull access for the cluster is NOT granted here. When AKS is
  # introduced (a later step), the cluster's *kubelet* identity (the identity
  # the nodes use to pull images — distinct from the cluster's control-plane
  # identity) is granted the built-in **AcrPull** role on this registry. That
  # role assignment lives with the AKS wiring, keeping this module focused on
  # the registry itself.
}
