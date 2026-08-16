# =============================================================================
# App Registration module — Entra ID application + service principal
# =============================================================================
# Registers the platform's API application in Microsoft Entra ID. Registering
# gives the app two things:
#   * an IDENTITY  — a client/application ID it authenticates as, and
#   * a CONTRACT   — the app roles it defines and the audience (identifier URI)
#                    that APIs validate incoming tokens against.

# The directory context Terraform is running in (used to set the app owner).
data "azuread_client_config" "current" {}

locals {
  # Derive the identifier URI from the display name if one wasn't supplied.
  identifier_uri = coalesce(var.identifier_uri, "api://${var.display_name}")

  # Fixed id for the delegated access_as_user scope. Referenced by the scope
  # definition AND by the test client's required_resource_access / pre-authorization,
  # so all three stay in lockstep on a stable GUID (a changed id would recreate the scope).
  access_as_user_scope_id = "3d0a3f4c-5e6f-6a7b-0c1d-2e3f4a5b6c7d"

  # Microsoft Graph (well-known) + its standard delegated OIDC scope ids. These GUIDs are
  # global constants, identical in every tenant.
  microsoft_graph_app_id        = "00000003-0000-0000-c000-000000000000"
  graph_openid_scope_id         = "37f7f235-527c-4136-accd-4a02d197296e"
  graph_profile_scope_id        = "14dad69e-099b-42c9-810b-d002981feec1"
  graph_offline_access_scope_id = "7427e0e9-2fba-42fe-b0c0-848c9e6a8182"
}

resource "azuread_application" "this" {
  display_name = var.display_name

  # identifier_uris is the AUDIENCE: tokens issued for this app carry this as
  # their `aud` claim, and the API validates it so a token minted for another
  # app can't be replayed against this one.
  identifier_uris = [local.identifier_uri]

  # The creating identity owns the app (so it can manage it afterwards).
  owners = [data.azuread_client_config.current.object_id]

  # App roles. Each role here surfaces in an issued token as a value in a FLAT
  # `roles` claim (e.g. "roles": ["admin"]). The application reads that claim to
  # authorize. (Note: some other identity providers nest role claims under a
  # different path — consumers must read the correct claim for the issuer.)
  dynamic "app_role" {
    for_each = var.app_roles
    content {
      id                   = app_role.value.id
      allowed_member_types = app_role.value.allowed_member_types
      description          = app_role.value.description
      display_name         = app_role.value.display_name
      value                = app_role.value.value
      enabled              = true
    }
  }

  # Expose the API with a delegated scope, so callers can request tokens for it.
  api {
    # requested_access_token_version = 2 issues v2 access tokens — the modern
    # Microsoft identity platform default. v2 emits cleaner claims (including a
    # flat `roles` claim), permits the friendly api://<display-name> identifier
    # URI (the tenant's identifier-URI policy rejects it under v1), and is the
    # version current application authentication libraries expect.
    requested_access_token_version = 2

    # access_as_user — the delegated scope a client requests to call the API on behalf of the
    # signed-in user. type = "User" means USERS can consent (not only admins); the user-consent
    # strings below complete that experience. Admins may still consent tenant-wide.
    oauth2_permission_scope {
      id                         = local.access_as_user_scope_id
      type                       = "User"
      value                      = "access_as_user"
      admin_consent_display_name = "Access the API as the signed-in user"
      admin_consent_description  = "Allow the application to access the API on behalf of the signed-in user."
      user_consent_display_name  = "Access the API on your behalf"
      user_consent_description   = "Allow the application to access the API on your behalf."
      enabled                    = true
    }
  }

  tags = var.tags
}

# A service principal makes the application a USABLE principal in this tenant —
# the local representation that can be assigned roles and hold tokens. The
# application is the global definition; the service principal is its instance
# in the directory.
resource "azuread_service_principal" "this" {
  client_id = azuread_application.this.client_id
}

# NOTE: no client SECRET is created here. This application is an API that
# VALIDATES tokens — it does not need a credential to request them. Callers that
# need to request tokens manage their own credentials (or use managed
# identities), keeping with the platform's secret-less posture.

# =============================================================================
# Public CLIENT application for interactive testing (e.g. Postman)
# =============================================================================
# Optional (var.create_test_client). This is the app a developer signs in AS to
# obtain a DELEGATED (user) access token for the API via OAuth2 Authorization
# Code + PKCE — no client secret. It is separate from the API app above: the API
# validates tokens; this client requests them on behalf of a user.
resource "azuread_application" "test_client" {
  count = var.create_test_client ? 1 : 0

  display_name = var.test_client_display_name
  owners       = [data.azuread_client_config.current.object_id]

  # fallback_public_client_enabled = true marks the app as a PUBLIC client (no secret): Entra
  # accepts the Authorization Code + PKCE token exchange without a client credential.
  fallback_public_client_enabled = true

  # PLATFORM CHOICE — the reply URL is registered under the PUBLIC CLIENT (mobile & desktop /
  # native) platform. This is a TRUE public client: Entra accepts the Authorization Code + PKCE
  # token exchange with NO secret (the web platform instead treats the app as CONFIDENTIAL and
  # demands a client_secret/client_assertion → AADSTS7000218). It also needs NO Origin header,
  # unlike single_page_application, which requires one that Postman's server-side callback does not
  # send → AADSTS9002327. Native + PKCE is the combination that works for Postman.
  public_client {
    redirect_uris = [var.test_client_redirect_uri]
  }

  # Delegated access to the API's access_as_user scope.
  required_resource_access {
    resource_app_id = azuread_application.this.client_id

    resource_access {
      id   = local.access_as_user_scope_id
      type = "Scope"
    }
  }

  # Microsoft Graph delegated openid / profile / offline_access — so the interactive sign-in
  # returns an id token and a refresh token alongside the API access token.
  required_resource_access {
    resource_app_id = local.microsoft_graph_app_id

    resource_access {
      id   = local.graph_openid_scope_id
      type = "Scope"
    }
    resource_access {
      id   = local.graph_profile_scope_id
      type = "Scope"
    }
    resource_access {
      id   = local.graph_offline_access_scope_id
      type = "Scope"
    }
  }

  tags = var.tags
}

# The client's service principal — its usable instance in this tenant.
resource "azuread_service_principal" "test_client" {
  count     = var.create_test_client ? 1 : 0
  client_id = azuread_application.test_client[0].client_id
}

# Pre-authorize the test client on the API's access_as_user scope. This grants consent in IaC so
# the interactive flow under an MFA-enforced tenant is NOT blocked on a user/admin consent prompt
# (the user still completes MFA; they just don't see a separate "grant permissions" screen).
resource "azuread_application_pre_authorized" "test_client" {
  count = var.create_test_client ? 1 : 0

  # v3 attribute names: application_id is the API application's resource id (/applications/<oid>);
  # authorized_client_id is the client being pre-authorized.
  application_id       = azuread_application.this.id
  authorized_client_id = azuread_application.test_client[0].client_id
  permission_ids       = [local.access_as_user_scope_id]
}
