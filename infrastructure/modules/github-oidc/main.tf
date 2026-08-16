# =============================================================================
# GitHub OIDC (CI/CD) module — resources
# =============================================================================
# The secret-less bridge between a GitHub Actions workflow and Azure, mirroring
# the workload-identity module (which does the same for AKS pods). For the CD
# pipeline this creates:
#   1. a user-assigned managed identity (UAMI) — a stable Azure identity with its
#      own client id, that a workflow authenticates AS via azure/login.
#   2. federated identity credential(s) — trust GitHub's OIDC issuer for one exact
#      workflow subject (a specific repo + ref, or repo + environment), so a run
#      matching that subject exchanges its short-lived GitHub OIDC token for an
#      Entra token. NO client secret is stored in GitHub (ADR-022).
#   3. the AcrPush role on the existing ACR — CD's only privilege: build and push
#      images. It gets NO cluster access, because deployment is GitOps (the
#      workflow updates Git and Argo CD deploys), so CD never touches the cluster.
#
# Identity type — UAMI vs Entra application + service principal:
#   ADR-022 allows "a federated credential on an Entra application (or
#   user-assigned managed identity)". A UAMI is chosen because it mirrors the
#   workload-identity module already in this repo, needs no app registration or
#   separately-managed service-principal object, and is the simplest thing that
#   federates. (An Entra application would only be preferable if the credential
#   had to be multi-tenant or carry API permissions/app roles — neither applies
#   to pushing an image.) The azuread provider is therefore not used here.

data "azurerm_client_config" "current" {}

# --- 1. One user-assigned managed identity for CI/CD -------------------------
# Named id-ak-cicd-<environment>, following the id-ak-<name>-<environment>
# convention the workload-identity module uses.
resource "azurerm_user_assigned_identity" "this" {
  name                = "id-ak-cicd-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

# --- 2. Federated identity credential(s) trusting GitHub's OIDC issuer --------
# The subject is an EXACT-MATCH string (like the workload-identity subject
# system:serviceaccount:<ns>:<sa>) — GitHub's OIDC token must present a `sub`
# claim identical to this or the token exchange is refused. The full subject is
#   repo:<github_org>/<github_repo>:<claim>
# where <claim> is one of GitHub's supported forms, e.g.:
#   ref:refs/heads/master        — runs on the master branch (push to master = CD)
#   environment:dev              — runs targeting the GitHub Environment "dev"
#   pull_request                 — runs from a pull_request event (CI does not need Azure)
# There is no wildcard: one credential per subject, so trust is scoped to exactly
# the refs/environments CD runs under. audience is the fixed Entra token-exchange
# audience GitHub's azure/login uses.
locals {
  # list -> map keyed by the caller-supplied short name, so for_each keys are
  # stable (reordering the list never forces a re-create) and each credential
  # gets a readable name.
  federated_subjects = {
    for s in var.subjects : s.name => "repo:${var.github_org}/${var.github_repo}:${s.claim}"
  }
}

resource "azurerm_federated_identity_credential" "this" {
  for_each = local.federated_subjects

  name                = "fic-ak-cicd-${each.key}-${var.environment}"
  resource_group_name = var.resource_group_name
  parent_id           = azurerm_user_assigned_identity.this.id
  audience            = var.audience
  issuer              = var.oidc_issuer
  subject             = each.value
}

# --- 3. AcrPush on the existing ACR ------------------------------------------
# CD's single privilege: push images to the registry. Mirrors the kubelet
# AcrPull assignment in the aks module — same scope input (the ACR id), same
# explicit principal_type to avoid the Entra replication race.
resource "azurerm_role_assignment" "acr_push" {
  scope                = var.acr_id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.this.principal_id

  # principal_type set explicitly so the provider does not look the principal up —
  # avoids a transient "principal not found" while the just-created UAMI's service
  # principal replicates across Entra (same rationale as the kubelet AcrPull
  # assignment and the role-assignments unit).
  principal_type = "ServicePrincipal"
}
