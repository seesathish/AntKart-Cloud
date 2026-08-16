# =============================================================================
# App Registration module — inputs
# =============================================================================

variable "display_name" {
  description = "Display name of the application registration (e.g. antkart-api-dev)."
  type        = string
}

variable "identifier_uri" {
  description = "The application's identifier URI — the audience APIs validate tokens against. Optional: if null, it is derived as api://<display_name>."
  type        = string
  default     = null
}

# App roles define the roles the application offers (e.g. admin, user). Each is
# assigned to callers and surfaces in their token's `roles` claim. The ids are
# FIXED GUIDs so they stay stable across applies (a changed id would recreate the
# role). allowed_member_types = ["User", "Application"] lets both users and
# service principals hold the role.
variable "app_roles" {
  description = "App roles the application defines. Each surfaces in issued tokens as a value in the flat `roles` claim."
  type = list(object({
    id                   = string
    display_name         = string
    description          = string
    value                = string
    allowed_member_types = list(string)
  }))
  default = [
    {
      id                   = "1b8f1e2a-3c4d-4e5f-8a9b-0c1d2e3f4a5b"
      display_name         = "Admin"
      description          = "Full administrative access to the API."
      value                = "admin"
      allowed_member_types = ["User", "Application"]
    },
    {
      id                   = "2c9f2f3b-4d5e-5f6a-9b0c-1d2e3f4a5b6c"
      display_name         = "User"
      description          = "Standard user access to the API."
      value                = "user"
      allowed_member_types = ["User", "Application"]
    }
  ]
}

variable "tags" {
  description = "Tags (categorization labels) applied to the application registration. azuread tags are a list of strings, not key/value pairs."
  type        = list(string)
  default     = []
}

# --- Optional public client (interactive testing, e.g. Postman) --------------
variable "create_test_client" {
  description = "Whether to also create a public-client application (no secret, PKCE) for interactive testing, plus its service principal and pre-authorization on the API's access_as_user scope."
  type        = bool
  default     = false
}

variable "test_client_display_name" {
  description = "Display name of the public test client application (used only when create_test_client = true)."
  type        = string
  default     = "ak-postman-test"
}

variable "test_client_redirect_uri" {
  description = "Redirect (reply) URI for the public test client. Registered under the web platform; default is Postman's hosted OAuth2 callback."
  type        = string
  default     = "https://oauth.pstmn.io/v1/callback"
}
