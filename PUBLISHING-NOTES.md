# Publishing notes

This repository is a public copy. The following identifiers appear in configuration and
are retained deliberately:

- The Entra tenant id and application client ids. A client id is not a credential: it
  authenticates nothing without a client secret or a federated trust relationship, and
  the platform stores neither. See the Security section of the README.
- The gateway's public ingress IP, which is already public via DNS.

No secret, connection string, access key, or certificate is present. Every credential
the platform uses is read from Azure Key Vault at runtime via workload identity.

Values that MUST be changed before this is deployed by anyone else are marked with
placeholder values and a comment: the budget notification email, the ACME account
email, and the PostgreSQL firewall address.
