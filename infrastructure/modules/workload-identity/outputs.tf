# =============================================================================
# Workload Identity module — outputs
# =============================================================================
# The Helm charts consume these: each service's Deployment sets its
# ServiceAccount's azure.workload.identity/client-id annotation to the client_id
# below. client_id and principal_id are identifiers, NOT secrets — the identity
# holds no credential to leak; trust comes from the OIDC federation, not a key.

output "identities" {
  description = "Map of service key => { client_id, principal_id, identity_name }. client_id is the value the ServiceAccount's azure.workload.identity/client-id annotation is set to."
  value = {
    for key, identity in azurerm_user_assigned_identity.this : key => {
      client_id     = identity.client_id
      principal_id  = identity.principal_id
      identity_name = identity.name
    }
  }
}
