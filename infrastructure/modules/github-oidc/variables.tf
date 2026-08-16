# =============================================================================
# GitHub OIDC (CI/CD) module — inputs
# =============================================================================
# Defines HOW the CI/CD identity is built: a user-assigned managed identity,
# federated to GitHub's OIDC issuer for one or more exact workflow subjects, and
# granted ONLY AcrPush on the existing registry. The environment supplies WHICH
# repo, WHICH subjects, and the ACR id; the module bakes in none of them.

variable "resource_group_name" {
  description = "Name of the resource group the identity is created in (from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the identity (from the Resource Group module's output)."
  type        = string
}

variable "environment" {
  description = "Environment name (e.g. dev), inherited from the root inputs. Used in resource names — the identity is named id-ak-cicd-<environment>."
  type        = string
}

variable "github_org" {
  description = "GitHub organisation/owner that owns the repository (the <org> in repo:<org>/<repo>:...). For this repo: seesathish."
  type        = string
}

variable "github_repo" {
  description = "GitHub repository name (the <repo> in repo:<org>/<repo>:...). For this repo: AntKart-Src3."
  type        = string
}

# The workflow subjects to trust. Each entry is one federated credential:
#   name  — a short, stable label used in the credential's resource name
#           (fic-ak-cicd-<name>-<environment>); must be unique in the list.
#   claim — the part after "repo:<org>/<repo>:" in GitHub's OIDC `sub` claim,
#           e.g. "ref:refs/heads/master" or "environment:dev". The module builds
#           the full exact-match subject "repo:<org>/<repo>:<claim>".
variable "subjects" {
  description = "List of GitHub OIDC subjects to federate. Each: { name = short label, claim = the suffix after repo:<org>/<repo>: }."
  type = list(object({
    name  = string
    claim = string
  }))
}

variable "oidc_issuer" {
  description = "GitHub Actions OIDC issuer URL. The federated credentials trust this issuer; do not change unless GitHub does."
  type        = string
  default     = "https://token.actions.githubusercontent.com"
}

variable "audience" {
  description = "Token-exchange audience the federated credentials require. api://AzureADTokenExchange is the fixed value azure/login requests."
  type        = list(string)
  default     = ["api://AzureADTokenExchange"]
}

variable "acr_id" {
  description = "Resource id of the existing Azure Container Registry (from the container-registry module's `id` output). Scope of the AcrPush role assignment."
  type        = string
}

variable "tags" {
  description = "Tags applied to the identity, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
