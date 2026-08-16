# ADR-003: Fault Tolerance with Polly

## Status
Accepted

## Context
AntKart is a distributed system: services talk to each other and to infrastructure (MongoDB, Redis, PostgreSQL, SQLite, RabbitMQ) and to third parties (Keycloak, Razorpay) over the network. In a single-process monolith a method call either runs or throws. In a distributed system the network is a participant — calls can be slow, time out, fail transiently, or hit a dependency that is temporarily down. Treating every such failure as a hard error means a 200ms blip in one dependency cascades into failed user requests across the platform.

Two failure characteristics drive the design:

- **Transient faults are common and self-healing.** A dropped TCP connection, a database failover, or a momentary GC pause in a downstream service usually clears within a second or two. Retrying often succeeds.
- **A genuinely-down dependency must not be hammered.** If AK.Discount is down, retrying every Products request 3× just multiplies load on a dead service, ties up threads waiting on doomed calls, and slows the caller. The system needs to *stop calling* a failing dependency and fail fast until it recovers.

These two needs (retry the blips, give up on the outages) are in tension, and resolving them by hand in every outbound call site does not scale across 8 services.

## Options Considered

### Option 1: No resilience — let exceptions propagate
Every transient network hiccup becomes a user-visible 500. Simple, but unacceptable for a platform where a single order touches Products, Discount, Payments, and the event bus. One flaky dependency degrades every flow that touches it.

### Option 2: Hand-rolled retry/breaker logic per call site
A `for` loop with `Task.Delay` for retries, a static counter and timestamp for a crude circuit breaker. Works for one call site, but:
- It is duplicated and subtly different in every service (different delays, no jitter, off-by-one retry counts).
- Circuit-breaker state (failure ratio over a sampling window, half-open probing) is genuinely hard to get right by hand.
- No jitter means all instances retry in lockstep after an outage — a thundering herd that knocks the recovering dependency straight back down.

### Option 3: Polly v8 via `Microsoft.Extensions.Resilience`
Polly is the de-facto .NET resilience library. Polly v8 is exposed through `Microsoft.Extensions.Resilience` and `Microsoft.Extensions.Http.Resilience` (9.0.0), which integrate with `IHttpClientFactory` and the DI container as composable **resilience pipelines** (retry, circuit breaker, timeout, fallback). The strategies are battle-tested, support jitter and half-open probing out of the box, and are configured declaratively.

## Decision
Use **Polly v8** through `Microsoft.Extensions.Resilience 9.0.0` and `Microsoft.Extensions.Http.Resilience 9.0.0`. Resilience is not configured ad-hoc per service — it is centralised in **three shared helpers** in `AK.BuildingBlocks/Resilience/ResilienceExtensions.cs` so every service applies the same, reviewed policy:

```csharp
// HTTP / gRPC outbound calls — Retry → Circuit Breaker → Timeout
builder.AddHttpResilienceWithCircuitBreaker(
    maxRetryAttempts: 3, failureRatio: 0.5,
    minimumThroughput: 5, breakDurationSeconds: 30);

// Redis (cart) — named pipeline "redis": retry 3× + 5s timeout
services.AddRedisResilience();

// PostgreSQL (orders, payments, notifications) — named pipeline "npgsql": retry 3× + 30s timeout
services.AddNpgsqlResilience();
```

### The HTTP pipeline (layers execute outer-to-inner on the way in, the reverse on the way out)
1. **Retry** — up to 3 attempts, `300ms × 2ⁿ` exponential backoff **with jitter**. Jitter (`UseJitter = true`) randomises each delay so multiple instances do not retry in lockstep after an outage (thundering-herd prevention).
2. **Circuit Breaker** — opens when ≥ 50% of requests fail across a 60s sampling window (once `MinimumThroughput` requests have been seen); stays open for 30s, then goes **half-open** and lets a single probe through to test recovery.
3. **Timeout** — a single attempt exceeding 15s is cancelled and counted as a failure for the breaker.

### Graceful degradation, not just failure
Resilience is paired with **fallbacks** where the business flow allows it. The clearest example: `DiscountGrpcClient` (AK.Products) catches `BrokenCircuitException` when the Discount circuit is open and returns a **zero-discount** result. The cart keeps working — discounts are skipped rather than the whole request failing. Fault tolerance means *degrade*, not *die*.

### Defence in depth at the edge
The API Gateway adds an **independent** circuit breaker per route via Ocelot `QoSOptions` (`ExceptionsAllowedBeforeBreaking`, `DurationOfBreak`, `TimeoutValue`). This is a second, coarser layer at the edge that is intentionally separate from each service's own in-process Polly pipeline.

### Resilience by layer

| Call path | Helper | Policy | Failure behaviour |
|-----------|--------|--------|-------------------|
| Products → Discount (gRPC) | `AddHttpResilienceWithCircuitBreaker` | Retry 3× expo+jitter, CB 50%/60s, 15s timeout | Circuit opens → zero-discount fallback; cart still works |
| Payments → Razorpay (REST) | `AddHttpResilienceWithCircuitBreaker` | Retry 3× expo+jitter, CB 50%/60s, 15s timeout | Retries exhausted / circuit open → 500 to client |
| UserIdentity → Keycloak (REST) | `AddHttpResilienceWithCircuitBreaker` | Retry 3× expo+jitter, CB 50%/60s, 15s timeout | Retries exhausted → auth error to client |
| ShoppingCart → Redis | `AddRedisResilience` | Retry 3× expo+jitter, 5s timeout | Retries exhausted → 503 to client |
| Order/Payments/Notification → PostgreSQL | `AddNpgsqlResilience` | Retry 3× expo+jitter, 30s timeout | Retries exhausted → 500 to client |
| Any → service (via Gateway) | Ocelot `QoSOptions` | Edge CB, per-route timeout | 503 at the edge, downstream protected |
| Any → RabbitMQ | MassTransit retry (see [ADR-007](ADR-007-masstransit-over-raw-rabbitmq.md)) | 3× incremental, then dead-letter | Message dead-lettered, not lost |

Note the deliberately different timeouts: Redis is 5s (a slow Redis almost always means a connection problem, so fail fast), while PostgreSQL is 30s (queries can legitimately take seconds under load). RabbitMQ resilience is owned by MassTransit, not Polly — see [ADR-007](ADR-007-masstransit-over-raw-rabbitmq.md).

## Consequences
**Easier:** Transient infrastructure blips no longer surface as user-facing errors. Outbound resilience is configured in three reviewed helpers rather than copy-pasted into every call site, so the policy is consistent and tunable in one place. Jitter prevents synchronised retry storms. A failing dependency is isolated by its circuit breaker instead of dragging down its callers, and where the domain permits (discounts) the system degrades gracefully rather than failing the whole request. The edge breaker and the in-process breaker give two independent layers of protection.

**Harder:** Resilience hides intermittent failures — a dependency can be quietly flaky for a long time while retries paper over it, so the policies must be paired with metrics/alerting on retry and circuit-breaker events (see [OBSERVABILITY.md](../guides/observability-concepts.md)) or real problems stay invisible. Retries make outbound calls **non-idempotent-unsafe**: any retried operation must be idempotent or guarded (this is why payment initiation and order creation rely on the outbox and saga rather than naive HTTP retries). Tuning the numbers (retry count, failure ratio, sampling window, break duration) is a per-dependency judgement call, and badly-tuned values either trip the breaker too eagerly or never at all. Developers must understand that a `BrokenCircuitException` is an expected, fast-fail signal — not a bug to be retried.

## Related
- [RESILIENCE.md](../guides/resilience-concepts.md) — full technical design, code locations, and the resilience architecture diagram
- [ADR-001: Microservices Architecture](ADR-001-microservices-architecture.md) — why the network is a participant in the first place
- [ADR-006: Ocelot API Gateway](ADR-006-ocelot-api-gateway.md) — the edge circuit breaker (QoS)
- [ADR-007: MassTransit over Raw RabbitMQ](ADR-007-masstransit-over-raw-rabbitmq.md) — messaging-layer retry and dead-lettering
