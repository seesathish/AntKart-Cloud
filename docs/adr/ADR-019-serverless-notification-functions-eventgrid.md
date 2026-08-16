# ADR-019 — Serverless Notification with Azure Functions and Event Grid

**Status:** Accepted (implemented)  
**Date:** 2026-06-02  
**Relates to:** ADR-017 (Entra ID, Azure Functions, Event Grid), ADR-015 (Service Bus messaging), ADR-014 (Cosmos DB and Service Bus provisioning)

> **Update — as built.** This ADR originally proposed triggering the notification Functions from a **Service Bus** subscription. The delivered implementation triggers them from **Event Grid** instead: AK.Order and AK.Payments publish discrete notification events to Event Grid as fire-and-forget side-effects **after** each durable commit, and the Functions are `[EventGridTrigger]`-bound. The old AK.Notification microservice and its Service Bus `notification` subscription were removed — notifications no longer consume the Service Bus integration-events topic. The reasoning below stands; only the notification trigger changed (Service Bus → Event Grid). The Service-Bus-vs-Event-Grid boundary table remains the guiding rule.

---

## Context

Notification delivery (order confirmation emails, payment receipts, cancellation notices, welcome emails) is a reactive, event-driven capability. It does no work of its own — it exists only to respond to events raised by other services. Its load profile is fundamentally different from the request-serving services:

- **Bursty and uneven.** A flash sale produces thousands of order-confirmation messages in minutes; the small hours of the morning produce none.
- **Idle most of the time.** For long stretches there are zero inbound events, yet an always-on consumer keeps a process resident, holding memory and a database connection while doing nothing.
- **Independently changeable.** An email-template fix has no bearing on the request-serving APIs and should not require redeploying them.

In the current in-process design, notification is handled by MassTransit consumers running inside an always-on container alongside a REST API (see ADR-015). That works, but it pays for idle compute and couples the notification logic's release cadence to the API's.

This ADR records the target-state decision to move the notification capability to an **event-driven serverless** model, and to clarify the boundary between the two eventing transports already in the platform — Azure Service Bus and Azure Event Grid. It complements ADR-017 (which covers identity migration alongside Functions and Event Grid); this record focuses specifically on the serverless notification and eventing decision.

---

## Options Considered

### Option A — Always-on containerized notification service

A dedicated service (container) hosting MassTransit consumers, running continuously on the cluster.

**Pros:**
- Identical programming model to the other services; no new hosting concept to learn.
- No cold starts — a consumer is always warm and ready.
- Full control over the runtime, long-running connections, and in-process caching.

**Cons:**
- Pays for compute 24/7 to serve a workload that is idle most of the time.
- Scaling out for a burst means provisioning and warming extra replicas, which reacts more slowly than per-message scale-out.
- Release cadence is coupled to whatever else shares the container; a template change forces a full redeploy.

### Option B — Azure Function (Consumption plan) triggered by messaging

An Azure Functions app whose functions are triggered by Service Bus (and, where appropriate, by Event Grid), billed per execution.

**Pros:**
- **Scales to zero** — no cost while idle, which matches the notification load profile directly.
- **Scales out per message automatically** — a burst of order confirmations fans out across many concurrent function instances without manual replica management.
- Independently deployable — the notification logic ships on its own cadence.
- The trigger binding replaces hand-written consumer host wiring; the business logic (template rendering, channel dispatch) is unchanged.

**Cons:**
- **Cold starts** — the first invocation after an idle period incurs added latency while the worker spins up. Acceptable for asynchronous email, where a sub-second-to-few-second delay is invisible to the user.
- A second hosting model to operate and reason about alongside the containerized services.
- Execution-time and connection limits require care for any long-running or stateful work (not a concern for short, stateless notification dispatch).

### Option C — Azure Logic Apps

A low-code workflow orchestrator with built-in connectors (including email and Service Bus).

**Pros:**
- Minimal code for simple "on message, send mail" flows; visual designer.
- Managed connectors remove some integration plumbing.

**Cons:**
- The notification logic (multi-channel resolution, templated rendering, persistence of a notification record, retention) already lives in tested .NET code; reimplementing it as a Logic App workflow discards that and splits logic across two paradigms.
- Harder to unit-test and version alongside the rest of the codebase.
- Per-action billing and connector constraints make non-trivial branching logic awkward and less transparent than code.

---

## Decision

Adopt **Azure Functions (isolated worker, Consumption plan)** as the hosting model for the notification capability, triggered by **Azure Event Grid** (the owning services publish discrete notification events as fire-and-forget side-effects after their durable commit), with the function dispatching through a reusable, channel-extensible notification core.

### Hosting and trigger model

- **Isolated worker, current .NET runtime.** The isolated model runs the function in its own process, decoupled from the Functions host runtime version, and is the forward-looking model for all new Functions (the in-process model is retired). It uses the standard `HostBuilder` DI container, so the notification core (channel abstraction, templates, history persistence) is registered and reused as-is.
- **Event Grid trigger.** The notification functions are `[EventGridTrigger]`-bound, one per customer notification event. The trigger replaces the always-on MassTransit consumer host; each function is thin — it deserializes the event and dispatches through `AK.Notification.Core`. (Notifications do **not** consume the Service Bus integration-events topic.)
- **Token-based authentication.** The function reaches Azure resources (ACS, Key Vault) with `DefaultAzureCredential` (no connection string), consistent with the platform-wide secret-less auth decision. Locally this resolves via the developer's CLI sign-in; in the cloud via the Function App's managed identity.

### Service Bus vs Event Grid — the transport boundary

The platform deliberately runs **both** transports, used for different semantics:

| Dimension | Service Bus (work / commands) | Event Grid (discrete reactive events) |
|---|---|---|
| Intent | "Do this unit of work" — a command directed at a known processor | "This happened" — a notification broadcast to whoever cares |
| Consumer model | Pull; consumer processes at its own pace | Push; routed to subscribers/endpoints |
| Coupling | Publisher targets a known topic/queue | Publisher emits an event; routing is decoupled from publishers |
| Filtering | Subscription rules on message properties | Event type, subject prefix, advanced filters |
| Ordering | FIFO sessions available | Not guaranteed |
| Retry / durability | Dead-letter queue, configurable retry | Timed retry window with backoff |
| Best fit | Ordered workflow steps, saga progression, guaranteed processing | Fan-out, cross-system reactive notification |

The guiding rule: **Service Bus carries work and workflow steps; Event Grid carries discrete "something happened" events that may fan out to multiple, evolving consumers.** Where Event Grid is used, routing it **through** a Service Bus queue (rather than to a raw HTTP webhook) adds a durable buffer — consumers pull at their own pace, survive restarts, and gain dead-lettering, without needing to expose a public endpoint.

### When a Function is the right choice vs a containerized service

| Choose a Function when… | Choose a containerized service when… |
|---|---|
| Work is event-triggered, short-lived, and stateless | Work serves synchronous request/response traffic |
| Load is bursty or mostly idle (scale-to-zero pays off) | Load is steady and a warm process is needed |
| Independent deploy cadence is valuable | Tight coupling to other in-process logic is acceptable |
| Cold-start latency is tolerable (async paths) | Consistent low latency is required on every call |

Notification sits squarely in the left column on every row, which is why it is the capability moved to serverless first.

---

## Consequences

### Positive

- **Scale-to-zero cost.** No compute is billed while no events arrive, directly matching the notification load profile.
- **Automatic per-message scale-out.** Bursts (e.g. a flash sale) fan out across function instances without manual replica provisioning.
- **Independent deployability.** Notification logic — including templates and channel changes — ships on its own cadence without redeploying the request-serving services.
- **Reuse, not rewrite.** The isolated-worker DI model reuses the existing channel abstraction, template renderer, and persistence; only the host/trigger changes.
- **Clear eventing boundary.** Codifying Service Bus (work) vs Event Grid (reactive broadcast) prevents the two from being used interchangeably and keeps fan-out concerns off the ordered-workflow path.

### Negative / Trade-offs

- **Cold starts.** The first invocation after idle adds latency. This is acceptable for asynchronous notification but would not be for a latency-sensitive synchronous endpoint — such workloads should remain containerized.
- **Two hosting models to operate.** The platform now runs both containers and Functions, increasing the operational surface (deployment, monitoring, and local-development tooling differ between the two).
- **Execution and connection limits.** The Consumption plan imposes execution-time and concurrency limits; long-running or stateful processing is unsuitable and must stay in a container. Notification's short, stateless dispatch fits comfortably within these limits.
- **Single active processor per event type.** A Function trigger and an always-on consumer can both subscribe to the same subscription and would each receive the message; in steady state exactly one processing path per event type should be active to avoid duplicate delivery.
- **Eventing requires discipline.** Running two transports only pays off if the Service-Bus-vs-Event-Grid boundary is respected; misusing Event Grid for ordered work (or Service Bus for open-ended fan-out) reintroduces the coupling each was chosen to avoid.
