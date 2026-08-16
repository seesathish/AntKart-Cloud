# ADR-021 — Retire the Dedicated Identity Service in Favour of Microsoft Entra ID

**Status:** Accepted
**Date:** 2026-06-09
**Area:** Identity Migration
**Relates to:** ADR-017 (Identity Provider, Functions, and Event Routing)

---

## Context

The application baseline included a dedicated identity microservice that acted as a thin proxy over a self-hosted identity provider: it handled login, registration, token refresh, a `/me` endpoint, and basic administrative operations (listing users, assigning roles). Every other service validated the tokens this provider issued.

The cloud-native platform delegates authentication to **Microsoft Entra ID**. Entra issues access tokens directly to clients through standard OAuth 2.0 / OpenID Connect flows, and each service is configured as a **pure token validator** (issuer, audience, lifetime, and signature, with authorization driven by the flat `roles` claim — established in the previous migration step). In this model, the responsibilities the identity service used to provide are met elsewhere:

- **Token issuance** is performed by Entra, not by an application service.
- **Token validation** is cross-cutting, handled by the shared authentication wiring in every service.
- **User lifecycle and app-role assignment** are operational concerns managed in Entra — through the portal or Microsoft Graph — rather than application features.

With those responsibilities relocated, a dedicated identity service no longer has a purpose.

## Decision

**Retire the dedicated identity microservice.** Specifically:

- Services authenticate callers by **validating Entra-issued tokens** via the shared building-blocks authentication; there is **no application-hosted identity service**.
- **User and role administration** is an operational activity carried out in **Entra / Microsoft Graph** (portal or Graph tooling), exercised during test enablement, not an application endpoint.
- The identity service's projects, its gateway route, and the provider-specific settings type that only it consumed are removed from the solution.

## Consequences

**Positive**

- **Simpler architecture** — one fewer service to build, deploy, operate, secure, and monitor.
- **No bespoke auth surface** — login, refresh, and admin flows are handled by a hardened, standards-based platform rather than application code.
- **Consistent validation** — every service trusts the same issuer and reads the same flat `roles` claim.

**Trade-offs**

- **Reliance on Entra** — authentication availability and configuration now depend on the managed identity platform; user and role administration moves to Entra/Graph tooling and processes.
- **Token acquisition for clients and tests** is performed through **standard Entra flows** (for example, requesting a token for the API's application ID URI), and callers must hold the relevant app-role assignment for the `roles` claim to be present.

## Notes

This is a deliberate simplification enabled by adopting a managed identity provider. The rendered C4 architecture diagrams still show the pre-migration topology (including the identity service) and are **to be updated post-migration**, once the migration round is complete.
