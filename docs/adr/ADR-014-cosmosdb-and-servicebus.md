# ADR-014 — Cosmos DB (MongoDB API, Serverless) and Azure Service Bus (Standard)

**Status:** Accepted  
**Date:** 2026-05-29  
**Area:** Data and Messaging Infrastructure  
**Relates to:** ADR-013 (Key Vault, Observability Foundation)

---

## Context

This step adds two production-grade managed services to AntKart's Azure infrastructure:

1. **Cosmos DB** — a globally distributed NoSQL database to replace the local Docker MongoDB container used during Phase 1 development
2. **Azure Service Bus** — a fully managed enterprise messaging service to replace the local RabbitMQ Docker container

Both services need to integrate with the Key Vault established earlier in the core infrastructure: connection strings are written as secrets and never hardcoded.

---

## Decision 1 — Cosmos DB with MongoDB API and Serverless Mode

### Decision

Use `kind = "MongoDB"` with `capabilities { name = "EnableMongo" }` and `capabilities { name = "EnableServerless" }`.

### Rationale

**Why MongoDB API, not Core SQL API?**

AK.Products is implemented with `MongoDB.Driver` against a MongoDB wire protocol. The MongoDB API is wire-compatible — no application code changes are needed when switching from a local `mongo:7` Docker container to Cosmos DB. Using the Core SQL API would require rewriting all Infrastructure-layer MongoDB queries to Cosmos DB SQL syntax.

**Why Serverless, not Provisioned Throughput?**

| Mode | Billing | When Best |
|------|---------|-----------|
| Serverless | Per RU consumed | Intermittent / unpredictable traffic |
| Provisioned | Per RU/s reserved | Steady, predictable, high-volume traffic |
| Autoscale | Per max RU/s scaled | Variable but sustained traffic |

Dev and staging workloads are intermittent: the database is idle most of the time and receives bursts during testing sessions. Serverless has **zero idle cost** — ideal for dev. There is a 5,000 RU/s burst limit per container, which is sufficient for development.

**Why Session consistency?**

Cosmos DB offers five consistency levels. For AntKart's development workload:
- `Strong` — highest consistency, highest latency, no global distribution benefit
- `Session` — reads your own writes within a client session; industry default for e-commerce
- `Eventual` — cheapest but stale reads can surface to users

Session consistency is the correct choice for AK.Products: a client that writes a product can immediately read it back correctly, while the cost is lower than Strong.

**Why `prevent_destroy = true`?**

Cosmos DB contains the production product catalogue. Unlike Service Bus (ephemeral messaging), accidental destruction of Cosmos DB destroys persistent data. The lifecycle guard requires an explicit `allow_destroy` flag to override.

### Consequences

- AK.Products needs an updated connection string in `appsettings.json` (or Key Vault override in AKS) pointing to the Cosmos DB endpoint
- Serverless mode cannot be changed to Provisioned Throughput without recreating the account — choose at account creation time
- Mongo server version `4.2` is the highest supported in Cosmos DB MongoDB API as of 2026

---

## Decision 2 — Azure Service Bus Standard SKU

### Decision

Use `azurerm_servicebus_namespace` with `sku = "Standard"`.

### Rationale

**Why Service Bus instead of keeping RabbitMQ?**

Phase 1 used a `rabbitmq:3-management` Docker container — correct for local dev (no internet dependency, zero cost). For Azure deployment:

| Concern | RabbitMQ self-hosted | Azure Service Bus |
|---------|---------------------|-------------------|
| Operations | You manage the broker cluster | Fully managed |
| SLA | Single container → no SLA | 99.9% (Standard) / 99.95% (Premium) |
| Azure Monitor integration | Manual exporter setup | Native diagnostic settings |
| Key Vault + Managed Identity | Custom implementation | Native support |
| MassTransit transport swap | No code change needed | No code change needed |

MassTransit (already used by AntKart for all event bus wiring) has first-class Service Bus support. Switching is a transport configuration change — consumer classes and integration events are unchanged.

**Why Standard, not Basic or Premium?**

| Feature | Basic | Standard | Premium |
|---------|-------|----------|---------|
| Queues | ✅ | ✅ | ✅ |
| Topics + Subscriptions | ❌ | ✅ | ✅ |
| Dead-lettering | ❌ | ✅ | ✅ |
| VNet integration | ❌ | ❌ | ✅ |
| Base cost | ~$0 | ~$10/month | ~$670/month |

AntKart's event-driven fan-out (`OrderCreated` → Products + Notification) requires **topics with multiple subscriptions**. Basic is disqualified. Premium is priced for production-grade private networking, far exceeding dev requirements.

**Why `local_auth_enabled = true`?**

The interim approach uses Shared Access Signature connection strings stored in Key Vault. This is acceptable for development. Later, the plan is to migrate to Managed Identity authentication for AKS pods — at that point `local_auth_enabled` should be set to `false` to disable SAS keys and enforce RBAC-only access.

**Why no `prevent_destroy`?**

Service Bus is stateless messaging infrastructure. Messages in transit during development are transient — destroying the namespace loses no persistent data. The ~$10/month base cost can be avoided by destroying the namespace between dev sessions and recreating it (takes ~60 seconds) when messaging is needed.

### Messaging Topology

```
[AK.Order] ──COMMAND──► order-commands (queue)
                              └──► [AK.Order consumer — one handler per message]

[AK.Order] ──EVENT──► integration-events (topic)
                            ├──► products-subscription ──► [AK.Products]
                            └──► notification-subscription ──► [AK.Notification]
```

- **Queue** (`order-commands`): Point-to-point commands — exactly one consumer processes each message
- **Topic** (`integration-events`): Pub/sub events — each subscriber gets an independent copy; adding a new subscriber requires only a new `azurerm_servicebus_subscription` resource

**Dead-lettering rationale:**

Both queue and subscriptions have `dead_lettering_on_message_expiration = true`. After `max_delivery_count = 10` failed delivery attempts, Azure moves the message to a dead-letter sub-queue automatically. This creates an audit trail for unprocessed messages that can be inspected and replayed from the Azure portal's Service Bus Explorer — critical for diagnosing production failures without losing the failed message.

### Consequences

- AK.Order, AK.Products, and AK.Notification need their `MassTransit` transport configuration updated from RabbitMQ to Service Bus (connection string from Key Vault)
- Standard SKU costs ~$10/month even when idle; destroy when not needed during development
- The connection string in Key Vault is updated automatically on each `terragrunt apply`; if the namespace is destroyed and recreated, the new connection string is written to Key Vault and services must restart to pick it up
- Premium SKU upgrade for production requires changing the `sku` variable and adding a private endpoint — no module restructure needed

---

## Alternatives Considered

### Cosmos DB: Core SQL API

Rejected — would require rewriting all AK.Products Infrastructure-layer queries from MongoDB Driver syntax to Cosmos DB SQL syntax. The MongoDB API provides the same managed-service benefits with zero application changes.

### Cosmos DB: Provisioned Throughput

Rejected for dev — requires a minimum of 400 RU/s even when idle, costing ~$23/month continuously. Serverless with zero idle cost is the correct choice for intermittent development workloads.

### Service Bus: Basic SKU

Rejected — no topics or subscriptions; cannot support the fan-out event pattern required for `OrderCreated → Products + Notification`.

### Service Bus: Azure Event Hub

Rejected — Event Hub is designed for high-throughput telemetry streaming (millions of events/second, Apache Kafka API). AntKart's business event volume is modest; Service Bus has richer messaging semantics (dead-lettering, sessions, scheduled delivery) that match the SAGA pattern better.

### Service Bus: Azure Event Grid

Rejected — Event Grid is a routing service for Azure resource events (infrastructure events like blob created, VM stopped). For application-level business events between microservices, Service Bus is the correct primitive.

---

## Cost Summary (Dev Environment)

| Service | Billing Model | Estimated Cost |
|---------|--------------|----------------|
| Cosmos DB (Serverless) | Per RU consumed | ~$0 idle; ~$1-3/month light dev use |
| Service Bus Standard | $10/month base + per-op | ~$10/month (or $0 if destroyed when idle) |
| **This addition** | | **~$0–13/month** |
| **Total dev infra** | | **~$15–28/month** |

Destroy Service Bus between sessions to keep costs near the lower bound.
