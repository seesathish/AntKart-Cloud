# ADR-005: SAGA Orchestration over 2PC and Choreography

## Status
Accepted

## Context
Placing an order requires coordinating three independent services: AK.Products (reserve stock), AK.Payments (process payment), and AK.ShoppingCart (clear cart on success). Each service has its own database, so distributed transactions (2PC — Two-Phase Commit) are not viable: they require a transaction coordinator, create locks across services, and fail catastrophically when any participant is unavailable.

Pure event choreography (each service reacts to events from others) was also evaluated. Choreography distributes the workflow logic across multiple services — there is no single place to look at to understand the full order flow, debugging a partial failure means tracing events across N service logs, and adding a new step (e.g. loyalty points) requires modifying multiple services.

## Decision
Use SAGA orchestration via MassTransit's `SagaStateMachine<OrderSaga, OrderSagaState>`. The `OrderSaga` in `AK.Order.Infrastructure` owns the entire order workflow state machine:

```
Initial
  → StockPending    (on OrderCreatedIntegrationEvent)
  → Confirmed       (on StockReservedIntegrationEvent)   → publishes OrderConfirmedIntegrationEvent
  → Cancelled       (on StockReservationFailedIntegrationEvent) → publishes OrderCancelledIntegrationEvent
```

Compensation flows handle each failure mode explicitly. The saga state is persisted in PostgreSQL via `MassTransit.EntityFrameworkCore` so the saga survives service restarts.

## Consequences
**Easier:** Single source of truth for order workflow state — one class, one state machine diagram. Easy to add new steps (loyalty points, fraud check) by adding new SAGA states without touching other services. Debugging is straightforward — check the saga state in the database.

**Harder:** More complex than choreography for simple flows. Requires MassTransit dependency in Order service. SAGA state table grows as order volume grows (mitigated by periodic archival). Developers must learn MassTransit's state machine DSL.
