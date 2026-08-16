# =============================================================================
# Terragrunt LIVE configuration — dev / Entra ID App Registration
# =============================================================================
# The deployable instance of the app-registration module for the dev
# environment. This unit creates a DIRECTORY object (an app registration) via
# the azuread provider.

# include "root": inherit the shared remote state backend (for Terraform state)
# and the shared provider generation. State still lives in the azurerm backend.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# NOTE: there is NO resource_group dependency here. An app registration is a
# DIRECTORY object — it lives in the Entra tenant, not inside a resource group —
# so it has no resource-group name or location to consume. This differs from
# every resource module so far, which all sit inside the resource group.

# terraform.source: the reusable app-registration module this unit runs.
terraform {
  source = "../../../modules/app-registration"
}

# inputs: this environment's values. The admin/user app roles come from the
# module default; only the display name is environment-specific here.
inputs = {
  display_name = "antkart-api-dev"

  # Also provision the public client for interactive (Postman) testing: a no-secret
  # PKCE app pre-authorized on the API's access_as_user scope, plus its service principal.
  create_test_client       = true
  test_client_display_name = "ak-postman-test"
  test_client_redirect_uri = "https://oauth.pstmn.io/v1/callback"

  tags = ["antkart", "dev"]
}
