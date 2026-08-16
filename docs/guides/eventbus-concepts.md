# AntKart — Event Bus Technical Design

> **⛔ SUPERSEDED — describes the Phase-1 platform running locally on Docker Compose.** Retained for historical reference. For the current cloud-native platform, see the [README](../../README.md) and the [Development Guide](../../DevelopmentGuide.md).

## Overview

Async communication between microservices uses **MassTransit 8.3.6** over **Azure Service Bus** as the transport. The order flow implements an **orchestrated SAGA** (a MassTransit state machine) with an **EF Core Outbox** to guarantee at-least-once delivery and prevent dual-write problems.

MassTransit keeps the consumers and saga **transport-agnostic** — they are written against MassTransit's API, so the move from a self-hosted broker to Service Bus changed only the bus configuration. The service connects to Service Bus with **Microsoft Entra authentication** (`DefaultAzureCredential`) against the namespace's fully-qualified hostname — there is no connection string or SAS key. The Service Bus **topology is owned by infrastructure-as-code** (see [Service Bus Topology & Observability](#service-bus-topology--observability)).

---

## Two Eventing Mechanisms

The platform deliberately runs **two** eventing transports, each for a different class of work. They are kept strictly separate — the distinction is by design, not accident. The decision is recorded in [ADR-019](../adr/ADR-019-serverless-notification-functions-eventgrid.md).

| | **Service Bus + saga** (this document's backbone) | **Event Grid + serverless Function** (side-effects) |
|---|---|---|
| **Carries** | Transaction-critical workflow: reserve stock → take payment → confirm | Discrete fire-and-forget reactions (e.g. notification) |
| **Model** | Pull-based; consumer processes at its own pace | Push-based; Event Grid delivers to the Function (the handler) |
| **Delivery** | Ordered, guaranteed; retried + dead-lettered until handled | Best-effort; failure is logged, never blocks the producer |
| **Correctness** | The order is wrong if a step is skipped | The order is correct whether or not the side-effect runs |
| **Hosting** | Always-on service (consumer/saga host) | Function App that **scales to zero**, billed per execution |
| **Auth** | Entra (`DefaultAzureCredential`), Data Sender/Receiver | Entra publish (EventGrid Data Sender); Function uses managed identity |

**The rule for choosing between them:**

> **Service Bus carries work and workflow steps that must complete for the transaction to be correct.** Use it when ordering, guaranteed processing, or saga progression matters.
>
> **Event Grid carries discrete "something happened" side-effects that must _not_ fail or delay the core transaction.** Use it when the reaction is optional to correctness, may fan out to multiple evolving consumers, and benefits from scale-to-zero serverless hosting.

**The decoupling guarantee (side-effect path).** A side-effect is published only **after** the durable commit, through `IEventGridSideEffectPublisher.TryPublishAsync` in `AK.BuildingBlocks.Messaging.EventGrid` — a helper whose contract is that **it never throws**: any publish failure is swallowed and logged, returning `false`. The `OrderConfirmedConsumer` updates the order to `Confirmed`, saves, and only then attempts the publish; a failure (or an unreachable topic) leaves the order confirmed and the saga unaffected. The Function (`AK.Notification.Functions`, .NET 9 isolated, `[EventGridTrigger]`) runs in a separate process — if it throws, only that one notification is lost. This is the structural opposite of the saga's at-least-once guarantee, and intentionally so.

---

## Event Flow

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
flowchart TD
    classDef service fill:#4A90D9,stroke:#2471A3,color:#fff
    classDef event fill:#3498DB,stroke:#2471A3,color:#fff
    classDef saga fill:#E67E22,stroke:#D35400,color:#fff,font-weight:bold
    classDef database fill:#27AE60,stroke:#1E8449,color:#fff
    classDef decision fill:#E74C3C,stroke:#C0392B,color:#fff

    Client([Client]):::service
    OrderAPI[AK.Order API]:::service
    Handler[CreateOrderCommandHandler]:::service
    MQ_OC{{Service Bus: order-created}}:::event
    Saga[OrderSaga]:::saga
    RSC[ReserveStockConsumer\nAK.Products]:::service
    MongoDB[(MongoDB)]:::database

    Client -->|POST /api/orders| OrderAPI
    OrderAPI -->|CreateOrderCommand| Handler
    Handler -->|save Order + Outbox| MQ_OC
    MQ_OC --> Saga
    MQ_OC --> RSC
    RSC -->|load & validate stock| MongoDB
    RSC -->|all stock OK| MQ_SR{{Service Bus: stock-reserved}}:::event
    RSC -->|stock insufficient| MQ_SRF{{Service Bus: stock-reservation-failed}}:::event

    MQ_SR --> Saga
    MQ_SRF --> Saga

    Saga -->|StockReserved| MQ_OConf{{Service Bus: order-confirmed}}:::event
    Saga -->|StockReservationFailed| MQ_OCan{{Service Bus: order-cancelled}}:::event

    MQ_OConf --> OCC[OrderConfirmedConsumer\nAK.Order]:::service
    MQ_OConf --> CCC[ClearCartOnOrderConfirmedConsumer\nAK.ShoppingCart]:::service
    MQ_OCan --> OCan[OrderCancelledConsumer\nAK.Order]:::service

    OCC -->|Status = Confirmed| OrderDB[(PostgreSQL\nAKOrdersDb)]:::database
    CCC -->|DeleteCart| Redis[(Redis)]:::database
    CCC -->|CartClearedIntegrationEvent| MQ_CC{{Service Bus: cart-cleared}}:::event
    OCan -->|Status = Cancelled| OrderDB
```

---

## Integration Events (AK.BuildingBlocks)

| Event | Publisher | Subscribers |
|-------|-----------|-------------|
| `OrderCreatedIntegrationEvent` | AK.Order (handler) | AK.Order (OrderSaga) |
| `StockReservedIntegrationEvent` | AK.Products | AK.Order (OrderSaga) |
| `StockReservationFailedIntegrationEvent` | AK.Products | AK.Order (OrderSaga) |
| `OrderConfirmedIntegrationEvent` | AK.Order (OrderSaga) | AK.Order (consumer), AK.ShoppingCart (consumer) |
| `OrderCancelledIntegrationEvent` | AK.Order (OrderSaga) | AK.Order (consumer) |
| `CartClearedIntegrationEvent` | AK.ShoppingCart | — |
| `PaymentInitiatedIntegrationEvent` | AK.Payments | — |
| `PaymentSucceededIntegrationEvent` | AK.Payments | AK.Order (updates status → Paid) |
| `PaymentFailedIntegrationEvent` | AK.Payments | AK.Order (updates status → PaymentFailed) |

All events implement `IIntegrationEvent` and are `sealed record` types in `AK.BuildingBlocks/Messaging/IntegrationEvents/`.

**Payment event payloads:**

| Event | Fields |
|-------|--------|
| `PaymentInitiatedIntegrationEvent` | `paymentId`, `orderId`, `userId`, `amount`, `currency`, `razorpayOrderId` |
| `PaymentSucceededIntegrationEvent` | `paymentId`, `orderId`, `userId`, `razorpayPaymentId` |
| `PaymentFailedIntegrationEvent` | `paymentId`, `orderId`, `userId`, `reason` |

---

## SAGA State Machine

Location: `AK.Order/AK.Order.Application/Sagas/OrderSaga.cs`

> The saga runs entirely within the **AK.Order** service, persisted to PostgreSQL via EF Core.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
stateDiagram-v2
    classDef sagaState fill:#E67E22,stroke:#D35400,color:#fff,font-weight:bold

    [*] --> StockPending : OrderCreated
    StockPending --> Confirmed : StockReserved\n→ publishes OrderConfirmed
    StockPending --> Cancelled : StockReservationFailed\n→ publishes OrderCancelled
    Confirmed --> [*] : saga complete
    Cancelled --> [*] : saga complete
```

**Correlation:** `OrderCreatedIntegrationEvent.OrderId` → `CorrelationId`

**State persistence:** PostgreSQL via EF Core (`order_saga_states` table), optimistic concurrency with `Version` column.

---

## Payment Event Flow

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
sequenceDiagram
    participant C as Client
    participant P as AK.Payments
    participant R as Razorpay
    participant MQ as Service Bus
    participant O as AK.Order

    C->>P: POST /api/payments/initiate
    P->>R: CreateOrder (amount, currency)
    R-->>P: razorpayOrderId
    P->>MQ: PaymentInitiatedIntegrationEvent\n(paymentId, orderId, userId, amount, currency, razorpayOrderId)
    P-->>C: 200 OK (razorpayOrderId, keyId)

    Note over C,R: Client completes payment on Razorpay checkout UI

    C->>P: POST /api/payments/verify\n(razorpayPaymentId, razorpayOrderId, razorpaySignature)
    P->>P: Verify HMAC-SHA256 signature

    alt Signature valid (happy path)
        P->>MQ: PaymentSucceededIntegrationEvent\n(paymentId, orderId, userId, razorpayPaymentId)
        MQ->>O: PaymentSucceededIntegrationEvent
        O->>O: Update Order.Status = Paid
        P-->>C: 200 OK (payment verified)
    else Signature mismatch (sad path)
        P->>MQ: PaymentFailedIntegrationEvent\n(paymentId, orderId, userId, reason)
        MQ->>O: PaymentFailedIntegrationEvent
        O->>O: Update Order.Status = PaymentFailed
        P-->>C: 400 Bad Request (signature invalid)
    end
```

---

## EF Core Outbox

The `OrderDbContext` includes MassTransit outbox entities:

```csharp
modelBuilder.AddInboxStateEntity();
modelBuilder.AddOutboxMessageEntity();
modelBuilder.AddOutboxStateEntity();
```

`CreateOrderCommandHandler` calls `IPublishEndpoint.Publish()` within the same EF Core transaction. MassTransit intercepts the call, stores the event in `outbox_message`, and delivers it after the DB commit — guaranteeing the order is never saved without the event being delivered.

---

## Service Bus Configuration

The transport is configured by a single **non-secret** setting — the namespace's fully-qualified hostname. There is no connection string or SAS key: the service authenticates to Service Bus with **Microsoft Entra** via `DefaultAzureCredential` (the developer's Azure CLI sign-in locally, the resource's managed identity in the cloud).

```json
"ServiceBus": {
  "FullyQualifiedNamespace": "sb-antkart-dev.servicebus.windows.net"
}
```

Each service binds **explicit receive endpoints** to the existing provisioned entities — it does **not** auto-create endpoints. A consuming service reads its own named **subscription** on the `integration-events` topic (`products`, `order`, `payments`, `cart`).

**Global retry policy** (configured in `MassTransitExtensions`):
- 3 retries with incremental back-off (1s, then +2s each) before the message is dead-lettered

---

## Service Bus Topology & Observability

### Topology is owned by infrastructure-as-code

The Service Bus entities are **provisioned by infrastructure-as-code** (the platform's source of truth — the Service Bus Terraform module), not created by the application at runtime:

- **A single `integration-events` topic.** Every integration event is published to this one shared topic. MassTransit's default topic-per-message-type naming is overridden so that all integration-event types map onto this single topic.
- **One named subscription per consuming service** on that topic — `products`, `order`, `payments`, and `cart`. Each subscription receives its own copy of every event (fan-out, not competing consumers), and the service reads its own subscription. (There is no `notification` subscription — notifications are serverless via Event Grid + Functions.)
- **An `order-commands` queue** for command-style messages with a single owner (point-to-point / competing consumers).

The application **conforms to** this topology — it never creates or manages it. The runtime identity holds only **Azure Service Bus Data Sender / Data Receiver** (never **Manage**), and the bus binds **explicit receive endpoints** to the existing entities. It does **not** call `ConfigureEndpoints` (which would auto-create MassTransit's conventional entities: a queue per consumer and a topic per message type). **Adding a new consumer or integration event that needs a new subscription or queue is an infrastructure change** — add the entity to the Service Bus Terraform module — **not a runtime concern.**

### Events and their consumers

| Integration event | Published by | Consumed by |
|-------------------|--------------|-------------|
| `OrderCreatedIntegrationEvent` | AK.Order | AK.Order (OrderSaga) |
| `StockReservedIntegrationEvent` / `StockReservationFailedIntegrationEvent` | AK.Products | AK.Order (OrderSaga) |
| `OrderConfirmedIntegrationEvent` | AK.Order (SAGA) | AK.Order, AK.Payments, AK.ShoppingCart |
| `OrderCancelledIntegrationEvent` | AK.Order (SAGA) | AK.Order |
| `PaymentSucceededIntegrationEvent` / `PaymentFailedIntegrationEvent` | AK.Payments | AK.Order |

Each consuming service receives events through its own subscription on the `integration-events` topic, so the same event reaches every interested service independently.

> **Notifications are not on this topic.** Customer notifications are not a Service Bus consumer. AK.Order and AK.Payments publish discrete **Event Grid** events (`AntKart.Order.Created/Confirmed/Cancelled`, `AntKart.Payment.Succeeded/Failed`) as fire-and-forget side-effects **after** each durable commit; the serverless `AK.Notification.Functions` consume those and dispatch through `AK.Notification.Core` (channel-extensible — Email/ACS now, WhatsApp/SMS later — with history persisted to `AKNotificationsDb`). See [Two Eventing Mechanisms](#two-eventing-mechanisms).

### Observing the flow

Inspect the live topology and message flow with the **Azure portal → Service Bus namespace** (Service Bus Explorer) or the Azure CLI:

- **Subscriptions / queue** — view active and dead-lettered message counts and peek messages on the `integration-events` topic's subscriptions and the `order-commands` queue.
- **Dead-letter sub-queue** — every queue and subscription has a built-in **dead-letter sub-queue (DLQ)**. After a message exceeds its max delivery attempts (the retry policy above) or its time-to-live, Service Bus moves it to the DLQ, where it can be inspected, the cause fixed, and the message resubmitted — nothing is silently lost.

---

### Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Messages accumulating on a subscription | Consumer service is down or erroring | Check the service's logs |
| A service cannot connect to Service Bus | Missing Entra role or no active sign-in | Ensure the identity holds Azure Service Bus Data Sender/Receiver on the namespace, and a token is available (`az login` locally / managed identity in the cloud) |
| Messages landing in the dead-letter sub-queue | Consumer threw repeatedly (poison message) | Inspect the DLQ payload and reason; fix the cause and resubmit |
| A consumer never receives events | Its subscription does not exist | Add the subscription in infrastructure-as-code (topology is IaC-owned, not created at runtime) |

---

## ReserveStockConsumer (AK.Products)

Location: `AK.Products/AK.Products.Application/Consumers/ReserveStockConsumer.cs`

1. Loads each product by ProductId from MongoDB
2. Validates all items have sufficient stock before applying any decrement (all-or-nothing)
3. On success: calls `Product.DecrementStock()` for each item, saves, publishes `StockReservedIntegrationEvent`
4. On failure: publishes `StockReservationFailedIntegrationEvent` with reason

> **Note:** MongoDB does not support multi-document transactions without a replica set. The consumer uses optimistic all-or-nothing validation before applying changes. `ConcurrentMessageLimit = 1` prevents race conditions in the test harness. For production at high scale, consider a MongoDB replica set.

---

## ClearCartOnOrderConfirmedConsumer (AK.ShoppingCart)

Location: `AK.ShoppingCart/AK.ShoppingCart.Application/Consumers/ClearCartOnOrderConfirmedConsumer.cs`

- Consumes `OrderConfirmedIntegrationEvent`
- Reads `UserId` from the event
- Calls `IUnitOfWork.Carts.DeleteAsync(userId)` if the cart exists
- Publishes `CartClearedIntegrationEvent`

---

## Order Consumers (AK.Order)

| Consumer | Event | Action |
|----------|-------|--------|
| `OrderConfirmedConsumer` | `OrderConfirmedIntegrationEvent` | Updates `Order.Status = Confirmed`, then **fires a fire-and-forget Event Grid side-effect** (`AntKart.Order.Confirmed`) for notification |
| `OrderCancelledConsumer` | `OrderCancelledIntegrationEvent` | Updates `Order.Status = Cancelled` |
| `PaymentSucceededConsumer` | `PaymentSucceededIntegrationEvent` | Updates `Order.Status = Paid` |
| `PaymentFailedConsumer` | `PaymentFailedIntegrationEvent` | Updates `Order.Status = PaymentFailed` |

These keep the Order aggregate's status in sync after the SAGA finalises or payment completes. `OrderConfirmedConsumer` additionally publishes a notification side-effect over Event Grid — but only **after** its durable commit, through the never-throws `TryPublishAsync`, so a publish failure can never roll back the confirmation (see [Two Eventing Mechanisms](#two-eventing-mechanisms)).

---

## MassTransit Registration

Each service registers via `AddAzureServiceBusMassTransit()` (BuildingBlocks helper). It takes **two callbacks**: the first **registers** the consumers / saga; the second **binds explicit receive endpoints** to the existing provisioned entities (a subscription on `integration-events`, and/or the `order-commands` queue). The helper connects with Entra auth, maps all integration events to the single `integration-events` topic, and does **not** auto-create topology.

```csharp
// AK.Products — reads the provisioned "products" subscription.
services.AddAzureServiceBusMassTransit(
    configuration,
    x => x.AddConsumer<ReserveStockConsumer>(),
    (context, cfg) =>
    {
        cfg.SubscriptionEndpoint("products", MassTransitExtensions.IntegrationEventsTopic, e =>
            e.ConfigureConsumer<ReserveStockConsumer>(context));
    });

// AK.Order — saga + event consumers read the provisioned "order" subscription.
services.AddAzureServiceBusMassTransit(
    configuration,
    x =>
    {
        x.AddSagaStateMachine<OrderSaga, OrderSagaState>()
         .EntityFrameworkRepository(r =>
         {
             r.ConcurrencyMode = ConcurrencyMode.Optimistic;
             r.ExistingDbContext<OrderDbContext>();
             r.UsePostgres();
         });
        x.AddEntityFrameworkOutbox<OrderDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
        x.AddConsumer<OrderConfirmedConsumer>();
        x.AddConsumer<OrderCancelledConsumer>();
        x.AddConsumer<PaymentSucceededConsumer>();
        x.AddConsumer<PaymentFailedConsumer>();
    },
    (context, cfg) =>
    {
        cfg.SubscriptionEndpoint("order", MassTransitExtensions.IntegrationEventsTopic, e =>
        {
            e.ConfigureSaga<OrderSagaState>(context);
            e.ConfigureConsumer<OrderConfirmedConsumer>(context);
            e.ConfigureConsumer<OrderCancelledConsumer>(context);
            e.ConfigureConsumer<PaymentSucceededConsumer>(context);
            e.ConfigureConsumer<PaymentFailedConsumer>(context);
        });
    });
```

`AK.Payments` and `AK.ShoppingCart` follow the same shape, each binding its own provisioned subscription (`payments`, `cart`). The `order-commands` queue is the provisioned command entity for command-style messaging; the current order workflow is event-driven and uses the `order` subscription.

---

## EF Core Migration

The `AddSagaAndOutbox` migration creates:

- `order_saga_states` — saga state table
- `InboxState` — MassTransit inbox deduplication
- `OutboxMessage` — outbox event store
- `OutboxState` — outbox delivery tracking

Run: `dotnet ef database update` from `AK.Order.Infrastructure` startup.
