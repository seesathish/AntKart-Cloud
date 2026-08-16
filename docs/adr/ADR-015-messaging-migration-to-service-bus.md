# ADR-015 — Messaging Migration: RabbitMQ → Azure Service Bus with Token Authentication

**Status:** Accepted  
**Date:** 2026-05-31  
**Area:** First Application Code Change  
**Relates to:** ADR-014 (Cosmos DB and Service Bus infrastructure provisioning)

---

## Context

Phase 1 of AntKart used RabbitMQ running as a `rabbitmq:3-management` Docker container on the developer's machine. Six services connected to it for all event-driven communication: Order, Products, Payments, Notification, ShoppingCart, and UserIdentity.

For Phase 2 (Azure deployment), we need to replace the local RabbitMQ broker with a managed messaging service. Azure Service Bus was provisioned as part of the core infrastructure. This ADR documents the decisions about *how* to migrate the application code to use it.

There were four distinct decisions to make:
1. Which authentication method: connection string or token-based?
2. Should the transport be configurable by environment, or replaced entirely?
3. Who should manage the Service Bus topology — Terraform or MassTransit?
4. What development model should be used going forward — local stack or cloud-connected?

---

## Decision 1 — Token-based authentication (DefaultAzureCredential), not connection strings

### Decision

Use `DefaultAzureCredential` from `Azure.Identity`. The Service Bus connection requires only the namespace FQDN (a non-secret). No connection string, no Shared Access Signature key.

### Rationale

**Connection string problems:**

A Service Bus connection string looks like:
```
Endpoint=sb://sb-antkart-dev.servicebus.windows.net;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<base64-key>
```

This is a long-lived credential. It must be stored securely, rotated periodically, transmitted between environments, and never committed to source control. A leaked key gives full access to the namespace until manually revoked. For six services that all need the same key, the operational surface area is large.

**Token auth advantages:**

- Tokens are short-lived (1 hour TTL), auto-refreshed by the SDK — a leaked token expires on its own
- No secret in any config file — only the namespace FQDN (public DNS name, not sensitive)
- Same code works locally (`az login` → `AzureCliCredential`) and in AKS (Workload Identity → `WorkloadIdentityCredential`)
- The identity that runs the service is the principal — RBAC controls what it can do, not a shared key

**DefaultAzureCredential credential chain:**

```
Local dev:   EnvironmentCredential → ... → AzureCliCredential ← wins (az login session)
In AKS:      EnvironmentCredential → WorkloadIdentityCredential ← wins (pod identity)
```

Same line of code in `MassTransitExtensions.cs`. Different credential source chosen at runtime based on environment. This is the modern Azure authentication pattern.

### Consequences

- Developer must have run `az login` and have an active session before running any service locally
- Developer identity must have `Azure Service Bus Data Owner` on the namespace (one-time RBAC grant)
- In AKS, a Managed Identity with the same role must be assigned to the pod
- No connection string in any committed configuration (`appsettings.json` or deployment config)

---

## Decision 2 — Replace RabbitMQ entirely; do not make the transport configurable by environment

### Decision

Remove `MassTransit.RabbitMQ` from all projects. The transport is Service Bus in all environments (local dev, staging, production). There is no environment flag to switch between transports.

### Alternatives considered

**Option A (rejected): Environment flag to switch transport**

```csharp
if (env.IsDevelopment())
    x.UsingRabbitMq(...);
else
    x.UsingAzureServiceBus(...);
```

This was rejected because:
1. It maintains two code paths — bugs in the Service Bus path might not surface until staging
2. It contradicts the enterprise model: develop against real cloud services for environment parity
3. It keeps `MassTransit.RabbitMQ` as a dependency and keeps the RabbitMQ container alive
4. Testing the retry policy, dead-lettering, and topology creation against RabbitMQ locally is not useful when production uses Service Bus

**Option B (chosen): Service Bus everywhere**

All environments use Service Bus. Locally, `DefaultAzureCredential` uses `AzureCliCredential`. This means a developer must have an Azure subscription and `az login` active — an acceptable requirement for enterprise cloud development.

### Consequences

- Developers need internet access and an active `az login` session to run services locally
- The RabbitMQ Docker container is removed from the local orchestration — one less container to start
- Any retry or dead-letter behaviour observed locally is identical to what will happen in production

---

## Decision 3 — Let MassTransit manage the Service Bus topology automatically

### Decision

Do not pre-create Service Bus topics and subscriptions via Terraform for MassTransit's use. Let `cfg.ConfigureEndpoints(ctx)` in MassTransit create and manage the topology at service startup.

### Rationale

MassTransit creates Service Bus entities named from the .NET message type's full name:
```
Topic: ak.buildingblocks.messaging.integrationevents:ordercreatedintegrationevent
Subscription: products-reserve-stock (for ReserveStockConsumer with prefix "products")
```

This is the standard MassTransit pattern. The alternative — mapping MassTransit consumers to hand-crafted Service Bus entities — requires explicit endpoint configuration per consumer and is fragile: names must be kept in sync between Terraform and C# code.

**Clarification on the pre-provisioned Terraform entities:**

The `order-commands` queue and `integration-events` topic created by the messaging Terraform module are teaching constructs. They illustrate the Queue vs Topic distinction for developers studying the platform. They are not used by MassTransit's runtime topology. Both coexist in the namespace — there is no conflict.

**Requirements for auto-topology:**

The running identity needs the `Manage` permission on the namespace. `Azure Service Bus Data Owner` includes Manage + Send + Listen. This is granted in Step 4.1 of `DevelopmentGuide.md`.

### Consequences

- Topics and subscriptions are created on first service startup, not at Terraform apply time
- Names are derived from .NET type names — if types are renamed, old subscriptions remain (orphaned) in the namespace and must be manually deleted
- The role assignment must be in place before any service starts for the first time

---

## Decision 4 — Adopt the enterprise local-against-cloud development model

### Decision

From this migration onwards, services are developed and debugged locally while connected to real Azure cloud services. The local Docker Compose stack is retained only for non-cloud infrastructure (PostgreSQL, Redis, Keycloak, Mailhog, Elasticsearch). RabbitMQ is removed from the compose file entirely.

### Rationale

The "run everything locally" model has a fundamental flaw: the local environment diverges from Azure in subtle ways. RabbitMQ and Service Bus have different dead-lettering semantics, different TTL handling, different session behaviour. Writing code against a local broker and discovering incompatibilities at deploy time is costly.

The enterprise model eliminates this class of problem:
- You test against the actual Service Bus instance with the actual Dead-letter settings
- Token auth (`az login`) is tested locally — the same flow that Workload Identity uses in AKS
- The retry policy is exercised against real Service Bus transient errors

**What remains local:**

| Service | Local or Cloud | Reason |
|---------|---------------|--------|
| Service Bus (messaging) | Cloud (Azure) | Enterprise model |
| Cosmos DB (products DB) | Cloud (Azure) | Enterprise model |
| Key Vault (secrets) | Cloud (Azure) | Enterprise model |
| PostgreSQL | Local Docker | No managed Azure PostgreSQL in free tier for dev; local is fine for schema work |
| Redis | Local Docker | Local Redis is identical to Azure Cache for Redis for this use case |
| Keycloak | Local Docker | No equivalent managed service in Azure free tier |
| Mailhog | Local Docker | Email trap; not a cloud migration concern |
| Elasticsearch | Local Docker | Used for log shipping; cloud version is a later phase |

### Consequences

- Developer onboarding requires Azure subscription access and RBAC role assignment (documented in DevelopmentGuide Step 4.1)
- Services cannot run fully offline (no internet → no Service Bus → no messaging)
- The same developer workflow will be used in AKS debugging via `kubectl port-forward`

---

## Summary of files changed

| File | Change |
|------|--------|
| `AK.BuildingBlocks/AK.BuildingBlocks.csproj` | Added `MassTransit.Azure.ServiceBus.Core 8.3.6`, `Azure.Identity 1.13.2`; removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.Order/AK.Order.Infrastructure/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.Products/AK.Products.Infrastructure/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.Payments/AK.Payments.Infrastructure/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.Notification/AK.Notification.Infrastructure/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.ShoppingCart/AK.ShoppingCart.Infrastructure/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.UserIdentity/AK.UserIdentity.API/*.csproj` | Removed `MassTransit.RabbitMQ 8.3.6` |
| `AK.BuildingBlocks/Messaging/MassTransitExtensions.cs` | Renamed method; replaced `UsingRabbitMq` with `UsingAzureServiceBus` + `DefaultAzureCredential`; reads `ServiceBus:FullyQualifiedNamespace` |
| `AK.Order/AK.Order.API/appsettings.json` | Replaced `RabbitMq` block with `ServiceBus.FullyQualifiedNamespace` |
| `AK.Products/AK.Products.API/appsettings.json` | Same |
| `AK.Payments/AK.Payments.API/appsettings.json` | Same |
| `AK.Notification/AK.Notification.API/appsettings.json` | Same |
| `AK.ShoppingCart/AK.ShoppingCart.API/appsettings.json` | Same |
| `AK.UserIdentity/AK.UserIdentity.API/appsettings.json` | Added `ServiceBus.FullyQualifiedNamespace` (no RabbitMq block to remove) |
| `AK.Order/AK.Order.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | `AddRabbitMqMassTransit` → `AddServiceBusMassTransit` |
| `AK.Products/AK.Products.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Same |
| `AK.Payments/AK.Payments.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Same |
| `AK.Notification/AK.Notification.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Same |
| `AK.ShoppingCart/AK.ShoppingCart.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Same |
| `AK.UserIdentity/AK.UserIdentity.API/Program.cs` | `AddRabbitMqMassTransit` → `AddServiceBusMassTransit` |
| Local orchestration config | Removed `rabbitmq` service, all `RabbitMq__*` env vars, `rabbitmq_data` volume; added `ServiceBus__FullyQualifiedNamespace` to 6 services |
| Local orchestration override | Removed `rabbitmq` port stanza |
| `CloudMigration.md` | Created — running record of all code changes with detailed reasoning |
| `DevelopmentGuide.md` | Added Section 4 — enterprise dev model, Service Bus explanation, token auth, step-by-step walkthrough |

**Zero changes to:** consumers, saga state machine, outbox configuration, integration event types, unit tests, integration tests (618 tests — all pass).
