# ADR-007: MassTransit over Raw RabbitMQ Client

## Status
Accepted

## Context
Services need asynchronous messaging for integration events (order placed, payment succeeded) and SAGA orchestration. The options were:
- **Raw RabbitMQ client** (`RabbitMQ.Client`) — maximum control, minimum abstraction
- **MassTransit** — transport-agnostic abstraction over RabbitMQ (and others), includes SAGA state machines, outbox pattern, consumer pipelines, retry policies

Using the raw client would mean building SAGA state machine infrastructure, outbox pattern, consumer pipeline retry logic, and dead-letter queue wiring manually — all non-trivial and well-trodden ground.

## Decision
Use MassTransit 8.3.6 with the RabbitMQ transport. Key features used:

- **`SagaStateMachine`** — orchestrates the order→stock→payment flow
- **`MassTransit.EntityFrameworkCore` outbox** — writes integration events atomically to the same PostgreSQL transaction as business data, preventing dual-write inconsistency
- **`AddRabbitMqMassTransit()` in BuildingBlocks** — a shared helper that registers MassTransit with service-prefixed queue names (e.g. `notification-payment-failed`, `order-payment-failed`) so multiple services can each receive every event independently (fan-out, not competing consumers)
- **Global retry** — 3 incremental retry attempts (1s/3s/5s delays) before dead-lettering

## Consequences
**Easier:** Switching message brokers (e.g. to Azure Service Bus for cloud deployment) requires only changing the transport registration — consumers are unchanged. Outbox prevents message loss on partial failure. MassTransit test harness enables in-memory integration testing without a running RabbitMQ instance.

**Harder:** MassTransit adds abstraction overhead. Developers must learn its conventions (endpoint naming, consumer registration, saga DSL). Version upgrades occasionally have breaking changes in the configuration API.
