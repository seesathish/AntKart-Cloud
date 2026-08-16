# =============================================================================
# App Registration module — outputs
# =============================================================================
# client_id is a REAL value consumed by the application's auth configuration in
# the data/identity migration phase (the audience/authority the API validates
# against). No secrets are output — this app has none.

output "client_id" {
  description = "The application (client) ID — used by the application's auth configuration."
  value       = azuread_application.this.client_id
}

output "object_id" {
  description = "The application object's id in the directory."
  value       = azuread_application.this.object_id
}

output "service_principal_object_id" {
  description = "The service principal's object id (the usable principal in this tenant)."
  value       = azuread_service_principal.this.object_id
}

output "app_role_values" {
  description = "The defined app role values (e.g. [\"admin\", \"user\"]) that appear in the token roles claim."
  value       = [for r in var.app_roles : r.value]
}

# --- Postman / interactive OAuth2 (Authorization Code + PKCE) -----------------
# These give everything Postman needs to obtain a delegated user token. The scope uses the
# registered App ID URI (api://<display-name>) — NOT api://<client-id> — because the requested
# scope string must match one of the app's identifier_uris, and the app cannot self-reference its
# own generated client id. The API still validates a token whose audience is either form
# (EntraSettings.ResolveValidAudiences accepts the App ID URI and the client-id GUID).

output "tenant_id" {
  description = "The Entra tenant (directory) id — the {tenant} segment of the authorize/token URLs."
  value       = data.azuread_client_config.current.tenant_id
}

output "postman_client_id" {
  description = "The public test client's application (client) id for Postman's Client ID field. Null when create_test_client = false."
  value       = one(azuread_application.test_client[*].client_id)
}

output "api_scope" {
  description = "The delegated scope Postman requests: api://<api-app-id-uri>/access_as_user."
  value       = "${local.identifier_uri}/access_as_user"
}

output "authorize_url" {
  description = "OAuth2 v2 authorization endpoint (Postman Auth URL)."
  value       = "https://login.microsoftonline.com/${data.azuread_client_config.current.tenant_id}/oauth2/v2.0/authorize"
}

output "token_url" {
  description = "OAuth2 v2 token endpoint (Postman Access Token URL)."
  value       = "https://login.microsoftonline.com/${data.azuread_client_config.current.tenant_id}/oauth2/v2.0/token"
}

output "redirect_uri" {
  description = "The registered reply URL Postman must use as its Callback URL."
  value       = var.test_client_redirect_uri
}
