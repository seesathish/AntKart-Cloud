# ADR-009: Domain Events vs Integration Events — Two Distinct Patterns

## Status
Accepted

## Context
DDD literature describes two event patterns that are easy to conflate:

- **Domain events** — intra-service signals. "Something meaningful happened inside this aggregate." Dispatched synchronously within the same process, consumed by other parts of the same bounded context (e.g. updating a read model, triggering a side effect in another aggregate in the same service).
- **Integration events** — inter-service signals. "Something happened that other services might care about." Published to a message broker (RabbitMQ), consumed asynchronously by any interested service.

Early in the project, integration events were the only mechanism used. Domain events were wired into the `Entity` base class (`AddDomainEvent()` / `ClearDomainEvents()`) but never dispatched — `ClearDomainEvents()` was called after `SaveChangesAsync()` without dispatching first.

## Decision
Maintain both patterns with explicit separation:

**Integration events** (primary cross-service mechanism) live in `AK.BuildingBlocks/Messaging/IntegrationEvents/`. Publishers call `IPublishEndpoint.Publish()` from command handlers. Consumers are MassTransit `IConsumer<T>` classes registered per-service.

**Domain events** (`IDomainEvent` records on entities) are the infrastructure for future intra-service side effects. Currently `ClearDomainEvents()` is called without dispatching — this is intentional, not a bug. When intra-service reactions are needed (Phase 2: read model projections, event sourcing), a `DomainEventDispatcher` using `IMediator.Publish(INotification)` will be added to the Unit of Work's `SaveChangesAsync()`, dispatching events before clearing them.

## Consequences
**Easier:** Integration events are the simple, proven path for cross-service communication today. The domain event infrastructure is ready without requiring all services to implement dispatching before it's needed.

**Harder:** Developers must understand why `ClearDomainEvents()` exists without dispatching — the code comment explains this is intentional. The future `DomainEventDispatcher` will require domain event records to also implement `MediatR.INotification`, adding a second interface to each event record.
