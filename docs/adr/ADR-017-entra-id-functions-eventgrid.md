# ADR-017 — Entra ID Migration, Azure Functions Isolated Worker, and Event Grid Routing

**Status:** Accepted  
**Date:** 2026-05-31  
**Area:** Azure Functions, Event Grid, and Identity Migration  
**Relates to:** ADR-015 (Service Bus token auth), ADR-016 (Workload Identity), ADR-012 (Terraform/Terragrunt)

---

## Context

AntKart's Phase 1 used Keycloak (Docker container) as the identity provider and MassTransit consumers (always-on processes) for event-driven notification delivery. Phase 2 targets Azure-managed equivalents.

Three decisions are covered in this ADR:

1. **Which Azure identity service replaces Keycloak?** — and what changes in the JWT validation pipeline
2. **Should the notification consumer become an Azure Function?** — and which hosting model
3. **Where does Event Grid fit alongside Service Bus?** — and which events belong on which transport

---

## Decision 1 — Replace Keycloak with Microsoft Entra ID (Azure AD)

### Decision

All seven services replace `AddKeycloakAuthentication()` with `AddEntraIdAuthentication()`. The authority is `https://login.microsoftonline.com/{tenantId}/v2.0`. Standard `AddJwtBearer()` is used (not `Microsoft.Identity.Web`) — the OIDC discovery endpoint auto-downloads signing keys. Audience validation is enabled. App roles (`user`, `admin`) are expressed as individual `roles` claims in the token and mapped to `ClaimTypes.Role` in `OnTokenValidated`.

`GetUserId()` in `HttpContextExtensions` reads the `oid` claim (Object ID) as the user identifier, falling back to the long-form URI `http://schemas.microsoft.com/identity/claims/objectidentifier`.

Two Entra ID app registrations are provisioned by the `infrastructure/modules/entra-id` Terraform module using the `hashicorp/azuread` provider:
- **AntKart-API**: resource server with app roles and an `access_as_user` OAuth2 scope
- **AntKart-SPA**: client app registration allowed to request tokens for AntKart-API

### Why Entra ID over alternatives

| Option | Rejected reason |
|---|---|
| Keep Keycloak in AKS | Additional infrastructure to manage; separate identity silo from the rest of Azure RBAC; no managed SLA |
| Auth0 / Okta | Third-party dependency; additional cost; doesn't integrate as tightly with Azure RBAC and Managed Identity |
| Microsoft.Identity.Web | Adds a higher-level abstraction that hides the underlying JWT Bearer config; makes it harder to understand what's actually happening; `AddJwtBearer()` is sufficient and more transparent |
| Azure AD B2C | Consumer-focused; more complex to configure app roles and admin flows; overkill for this platform's requirements |

### Why `oid` instead of `sub` as the user identifier

In Entra ID, the `sub` claim is **pairwise** — it is different for each application. If a user has tokens from AntKart-API and from another app in the same tenant, their `sub` values are different. The `oid` (Object ID) is **stable across all apps in the tenant**: it is the directory object ID of the user and never changes.

Using `oid` as the user identifier ensures that:
- Cross-service comparisons (e.g. "does this order belong to this user?") remain correct even if the auth client changes
- IDOR protection is preserved: `GetUserId()` reads from the validated JWT; the client cannot supply a different user's `oid` in the request body

This reverses the earlier Keycloak convention where `sub` was used because Keycloak's `sub` was the stable Keycloak UUID.

### Why standard `AddJwtBearer()` and not `Microsoft.Identity.Web`

`Microsoft.Identity.Web` is a convenience wrapper that adds middleware for token caching, on-behalf-of flows, and incremental consent — features that are either not needed (no token caching for stateless REST APIs) or not applicable (we don't do OBO in the notification or shopping cart service). Standard `AddJwtBearer()` with Entra's OIDC discovery URL provides everything needed:

- Automatic signing key rotation via metadata endpoint
- Audience and issuer validation
- `ClaimsIdentity` population from the JWT payload

The `OnTokenValidated` event manually maps `roles` claims → `ClaimTypes.Role` because Entra does not automatically map app role claims to the ASP.NET Core role claims principal — it emits them as `roles` strings, one per role value.

### Why audience validation is now enabled (was disabled for Keycloak)

The Keycloak integration disabled audience validation and instead checked the `azp` (authorized party) claim because Keycloak did not set `aud` consistently. Entra ID reliably sets `aud` to `api://{clientId}` for tokens issued for app registrations with custom scopes. Enabling validation means a token stolen from a different app (with a different `aud`) is rejected.

---

## Decision 2 — Azure Function (isolated worker, .NET 9) for notification delivery

### Decision

`AK.Notification.Functions` is a new Azure Functions isolated worker project targeting `net9.0`. It contains one function, `OrderCreatedNotificationFunction`, triggered by a Service Bus topic subscription. The function delegates to `SendNotificationCommand` via MediatR — the same code path as the existing `OrderCreatedConsumer`. The project uses `AddInfrastructureCore()` (database, channels, template renderer) but not `AddInfrastructure()` (which adds MassTransit consumers) — the trigger replaces the consumer.

The project uses `Microsoft.Azure.Functions.Worker.Sdk` version 2.0.7 (not 1.17.4) because the 1.x SDK does not define the `_ToolingSuffix` MSBuild property for `net9.0`, causing a pre-build error.

### Why isolated worker (not in-process)

| Model | Isolated worker | In-process |
|---|---|---|
| .NET version | Any .NET (8, 9, future) | Must match the Functions host (.NET 6) |
| Process boundary | Separate process; no shared AppDomain | Same process as the host |
| DI | Full `HostBuilder` with all extensions | Limited to Functions extensions |
| Debug | Full .NET debugging | Functions host process debugging |
| Status | Current model (in-process deprecated) | Deprecated as of Functions v4 |

The isolated model is the forward-looking choice for all new Functions.

### Why a Function and not only a MassTransit consumer

The MassTransit consumer in the Notification API runs in the same always-on process as the REST API. For an e-commerce notification service, most hours of the day have zero inbound messages — the process runs, consumes memory and compute, and does nothing.

An Azure Function on the Consumption plan:
- **Scales to zero** when no messages arrive — no idle cost
- **Scales out automatically** when a burst of messages arrives (e.g. a flash sale triggers thousands of order confirmations)
- **Is independently deployable** — a bug fix in the email template does not require redeploying the API

The Function and the MassTransit consumer can coexist: both subscribe to `notification-subscription`. The Function is the demonstration of the serverless pattern; the consumer continues to handle the other event types (PaymentSucceeded, OrderCancelled, etc.).

### Why `DefaultAzureCredential` for Service Bus (no connection string)

The `ServiceBusConnection__fullyQualifiedNamespace` setting in `local.settings.json` (double-underscore form) tells the Functions host to authenticate via `DefaultAzureCredential`. Locally, `az login` resolves the credential. In Azure, the Function App's system-assigned managed identity resolves it (once `Azure Service Bus Data Receiver` role is assigned via Terraform).

This is consistent with ADR-015's decision to use token auth for all Service Bus connections.

---

## Decision 3 — Event Grid custom topic for UserRegistered event routing

### Decision

A new `infrastructure/modules/eventgrid` Terraform module creates:
- An Event Grid custom topic (`egt-antkart-{env}`)
- An event subscription filtering on event type `AntKart.UserRegistered` and routing to the Service Bus queue `user-registered-events`

Applications that need to react to user registration pull from the Service Bus queue; they are not required to know about Event Grid.

### Why Event Grid in addition to Service Bus

Service Bus and Event Grid are complementary, not competing:

| Dimension | Service Bus (existing) | Event Grid (new) |
|---|---|---|
| Initiator | Publisher sends to a known topic/queue | Any Azure service or application publishes to a topic; Event Grid routes it |
| Consumer model | Pull — consumer polls at its own pace | Push — Event Grid delivers to an endpoint or resource |
| Filtering | Subscription rules on message properties | Event type, subject prefix, advanced filter expressions |
| Ordering | FIFO sessions available | Not guaranteed |
| Retry | Dead-letter queue, configurable retry policy | 24-hour retry window with exponential backoff |
| Best fit | Workflow steps, commands, ordered processing | Reactive fan-out, cross-service event notification |

The UserRegistered event is a notification ("a user was created") rather than a command ("assign this user to a group"). It is broadcast-oriented: in the future, multiple systems (CRM, analytics, loyalty points) may react to it. Event Grid's fan-out and filtering capabilities are better suited than Service Bus topic subscriptions for this pattern.

The Event Grid → Service Bus routing (rather than Event Grid → HTTP webhook) adds a durable buffer: consumers pull from the queue at their own pace, survive restarts, and benefit from dead-lettering — without requiring a public HTTPS endpoint.

### Why this module depends on the servicebus module

The `user-registered-events` Service Bus queue is created by the `servicebus` module (added when the messaging module was introduced). The `eventgrid` Terragrunt wiring uses a `dependency` block to get the queue's resource ID (`user_registered_queue_id` output). This ensures correct deployment order and prevents the event subscription from referencing a non-existent queue.

---

## Consequences

### Positive

- **Managed identity everywhere:** No long-lived secrets for auth (Keycloak service account replaced by Entra credentials; Service Bus uses managed identity token auth)
- **IDOR safety preserved:** `GetUserId()` still reads from the validated JWT; client cannot influence the user identifier; the only change is `sub` → `oid` which is *more* stable, not less
- **Serverless scale-to-zero:** Notification processing incurs no cost during idle periods
- **Standard Entra integration:** Enables future Conditional Access, MFA, B2B guest access without changing application code
- **Stable app role GUIDs:** Fixed UUID defaults in Terraform variables prevent drift in `appRoleAssignment` records

### Negative / Trade-offs

- **ROPC flow for local dev:** The Resource Owner Password Credentials grant (used in the E2E runbook) is deprecated by Microsoft. It works for local testing but must not be used in production UIs — the SPA must use Authorization Code + PKCE
- **Client secret for Graph API:** `EntraIdService` uses a client secret to get app-level tokens for Microsoft Graph. Unlike the Cosmos DB and Service Bus connections, this secret cannot be replaced by a managed identity from a developer machine. For production, it should be stored in Key Vault and retrieved at startup (pattern established in ADR-016)
- **Worker SDK 2.x version jump:** Upgrading from Worker SDK 1.17.4 to 2.0.7 is a major version bump. Breaking changes in the 2.x series require validation — for this project the isolated worker API surface used (HostBuilder, ServiceBusTrigger, FunctionContext) was unchanged
- **Duplicate notification processing:** While both the MassTransit consumer and the Azure Function subscribe to `notification-subscription`, they both receive the same message. In the current setup the consumer handles all event types and the Function handles OrderCreated only — so there is no actual duplicate delivery for OrderCreated messages if only one is active at a time. In production, only one processing path per event type should be active
