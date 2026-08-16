# AntKart — Observability Technical Design

> **⛔ SUPERSEDED — Phase-1 design; the specifics here do NOT describe the delivered platform.** This file predates the delivered observability work, and several details are now wrong: the query tables, the KQL, and the App-Insights-collects-logs claim. In reality, the AKS services' logs are collected by the OMS agent into **Log Analytics (`ContainerLog`)** — **not** Application Insights — and OpenTelemetry traces (a Phase-1 gap) are now exported to Application Insights (`AppRequests`/`AppDependencies`). Retained for historical reference only. For how observability actually works today, and the **working** KQL, see [Observability — how it is seen](../development/5-observability.md) and [ADR-025 — Observability Architecture](../adr/ADR-025-observability-architecture.md).

## Overview

All AntKart services emit **structured logs via Serilog**, written to the **Console** (the standard sink for containers and serverless) and a local rolling file for development. In the cloud the console stream is collected by **Azure Monitor — Application Insights / Log Analytics**, which is the query and dashboard surface. There is **no Elasticsearch/Kibana sink**.

---

## Sinks

| Sink | Where | Purpose |
|------|-------|---------|
| Console | every service / function | Structured output; collected by Application Insights / Log Analytics in the cloud |
| Rolling file (`logs/`) | local development | Convenience for local runs (7-day retention) |

The cloud telemetry path is provided by **Application Insights** (wired via its connection string on the Function App and the AKS workloads), not by a Serilog sink configured in code.

---

## Services Emitting Logs

| Service | ServiceName | Notes |
|---------|-------------|-------|
| AK.Gateway | AK.Gateway | Ocelot edge routing |
| AK.Products | AK.Products.API | Cosmos DB (MongoDB API) |
| AK.ShoppingCart | AK.ShoppingCart.API | Redis |
| AK.Order | AK.Order.API | PostgreSQL + SAGA |
| AK.Payments | AK.Payments.API | PostgreSQL + Razorpay |
| AK.Discount | AK.Discount.Grpc | gRPC · **PostgreSQL** (SQLite in Phase-1; migrated in the data-tier move) |
| AK.Notification.Functions | AK.Notification.Functions | Serverless notifications (Event Grid-triggered) |

---

## Serilog Setup (BuildingBlocks)

`SerilogExtensions.AddSerilogLogging()` configures the Console + rolling-file sinks and enriches every entry:
- `ServiceName` — from `IHostEnvironment.ApplicationName`
- `Environment` — from `ASPNETCORE_ENVIRONMENT`
- `CorrelationId` — from the `X-Correlation-Id` header (via `CorrelationIdMiddleware`)

No log-store URL or sink credentials are configured in code — the cloud collector (Application Insights) reads the console stream.

---

## Structured Log Examples

### Order Service
```
OrderId={OrderId} UserId={UserId} Status={Status}
```

### Payments Service
```
PaymentId={PaymentId} OrderId={OrderId} RazorpayOrderId={RazorpayOrderId}   ← payment initiated
PaymentId={PaymentId} verified via Razorpay                                  ← payment succeeded
PaymentId={PaymentId} reason={Reason}                                        ← payment failed
```

---

## Querying logs

> **The KQL that was here did not work against the delivered platform and has been removed.** It queried the Application Insights `traces` table filtered on `customDimensions.ServiceName`, which returns **zero rows**: the AKS services do not write logs to Application Insights (only `func-antkart-notifications-dev` does), and the delivered log table is **`ContainerLog`**, not `traces`. For the **working** queries — structured logs via `parse_json(LogEntry)` on `ContainerLog`, and traces via `AppRequests`/`AppDependencies` — see [Observability — how it is seen → Working KQL](../development/5-observability.md#working-kql).

Locally, the Console sink prints the same structured entries (and the rolling file under `logs/`).

---

### What each service logs

| Service | Key log events |
|---------|---------------|
| AK.Gateway | Incoming requests, route matches, rate-limit rejections |
| AK.Products | Product queries, stock reservation start/success/failure |
| AK.ShoppingCart | Cart read/write, cart cleared on order confirmed |
| AK.Order | Order created, SAGA state transitions, status updates, notification side-effect published |
| AK.Payments | Payment initiated, signature verified/failed, events published |
| AK.Notification.Functions | Event received, dispatch result, email send outcome |

---

## Health Checks

Every service exposes **three probe surfaces** plus a backward-compatible alias, wired identically via `AddDefaultHealthChecks()` + `MapDefaultHealthChecks()` (BuildingBlocks). The Kubernetes probes that consume them are connected in the AKS milestone; these endpoints are what they target.

| Endpoint | Selects | Maps to | Job |
|----------|---------|---------|-----|
| `GET /health/live` | `ak:live` checks (shallow `self`) | Liveness probe | "Process responsive?" Makes **no external calls** — a failed liveness probe restarts the pod, so checking a dependency here risks a **restart storm**. |
| `GET /health/ready` | `ak:ready` checks | Readiness probe | "Take traffic?" **Tolerant** — Degraded ⇒ HTTP 200 (still serving). A failed readiness probe de-registers the pod, so failing on a shared-dependency blip risks a fleet-wide **blackout**. |
| `GET /health/deps` | **all** checks (incl. deep + MassTransit bus) | Diagnostics (humans/dashboards) | Deep reachability (Cosmos, Service Bus, Key Vault), detailed JSON. **Not** a probe — may go red without restarting/de-registering anything. |
| `GET /health` | `ak:live` (shallow alias) | Legacy monitors | Unchanged shallow behaviour. |

**Tags are namespaced (`ak:`) on purpose.** Third-party libraries register their own checks with plain tags — MassTransit tags its bus check `"ready"` — so selecting by `ak:ready` keeps our readiness probe from silently inheriting a shared dependency's state. Deep checks (`MongoDbHealthCheck` = a real Cosmos `{ ping: 1 }`; reusable `KeyVaultHealthCheck` = lists secret *metadata* only) are tagged `ak:deep` and surface **only** on `/health/deps`. The gateway proxies `/health` for each downstream service.

---

## Correlation IDs

`CorrelationIdMiddleware` (BuildingBlocks) reads or generates `X-Correlation-Id` on every request. The Gateway forwards the header downstream, so a single client request can be traced across all service logs by filtering on `CorrelationId`.
