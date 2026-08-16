# =============================================================================
# Terragrunt LIVE configuration — dev / GitHub OIDC (CI/CD)
# =============================================================================
# The deployable instance of the github-oidc module for the dev environment. The
# module says HOW to build the CI/CD federated identity; this file says WHICH
# GitHub repo and WHICH workflow subjects to trust, and wires the AcrPush scope
# to the real registry id.
#
# What this identity is for: the CD (delivery) workflow authenticates AS this
# identity via OIDC (azure/login) to push images to ACR. It gets AcrPush and
# nothing else — deployment is GitOps (the workflow updates Git; Argo CD deploys),
# so CD needs no cluster access and holds no stored secret (ADR-022 / ADR-023).
#
# Scope note — why only resource-group and container-registry are dependencies:
# the identity is created in the resource group and granted AcrPush on the ACR.
# It touches no data store, Service Bus, Event Grid, or the cluster, so those
# units are deliberately not wired here.

# include "root": inherit the shared remote-state backend, the shared azurerm
# provider, and the environment = "dev" input (used in the identity name).
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# --- Upstream dependencies ---------------------------------------------------
# One dependency per output this unit consumes. Each mock lets init/plan/validate
# run before the upstream units have been applied (clean checkout or CI).

# The resource group the identity is created in.
dependency "resource_group" {
  config_path = "../resource-group"
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# The container registry — supplies the id that scopes the AcrPush role assignment.
dependency "container_registry" {
  config_path = "../container-registry"
  mock_outputs = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-antkart-dev-eastus/providers/Microsoft.ContainerRegistry/registries/acrantkartdev"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable github-oidc module this unit runs.
terraform {
  source = "../../../modules/github-oidc"
}

# inputs: the GitHub repo and the exact workflow subjects to federate, plus the
# real ACR id for the AcrPush scope. (environment = "dev" is inherited from root.)
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  github_org  = "seesathish"
  github_repo = "AntKart-Src3"

  # The subjects CD actually runs under. Full subject = repo:<org>/<repo>:<claim>:
  #   master  -> repo:seesathish/AntKart-Src3:ref:refs/heads/master
  #              (a push to master triggers CD)
  #   env-dev -> repo:seesathish/AntKart-Src3:environment:dev
  #              (used if the CD job declares `environment: dev`, so the token is
  #               scoped to that GitHub Environment)
  subjects = [
    { name = "master", claim = "ref:refs/heads/master" },
    { name = "env-dev", claim = "environment:dev" },
  ]

  acr_id = dependency.container_registry.outputs.id

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
