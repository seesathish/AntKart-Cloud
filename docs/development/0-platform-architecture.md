# Platform architecture — how the code is built

> **Diagrams pending review:** _Inside AK.Order_ and _Order saga_ are carried across as-is and will be reworked.

This layer is the engineering foundation: how a single AntKart service is structured and how the services coordinate. It is the same across all six services, so understanding one is understanding all.

Every service applies **Clean Architecture** and **Domain-Driven Design** — a `Domain` core with no outward dependencies, an `Application` layer of use cases (**CQRS** commands and queries dispatched by **MediatR** through a validation pipeline), an `Infrastructure` layer for persistence and messaging, and a thin **minimal-API** (or gRPC) host. Services never call each other synchronously for business flows; they coordinate through an **orchestrated saga** on Azure Service Bus, with a **transactional outbox** so a state change and its event are written atomically. Persistence hides behind **repository + specification + unit of work**.

AK.Order is the richest example — CQRS via MediatR, saga orchestration, EF Core outbox, and a domain state machine that enforces valid order transitions. Commands flow through a `ValidationBehavior` pipeline; `CancelOrder`/`UpdateOrderStatus` return `Result<T>` for expected failures while `CreateOrder` throws for the unexpected.

## Inside AK.Order

```mermaid
flowchart TD
    subgraph API["AK.Order.API — minimal-API host"]
        EP["OrderEndpoints<br/>GET /api/orders · /me · /{id}<br/>POST · PUT /{id}/status · DELETE"]
    end

    subgraph APP["AK.Order.Application — CQRS use cases"]
        MED["IMediator · MediatR"]
        VB["ValidationBehavior&lt;TRequest,TResponse&gt;<br/>FluentValidation pipeline"]
        subgraph SLICES["Features — vertical slices"]
            CO["CreateOrderCommandHandler"]
            UOS["UpdateOrderStatusCommandHandler"]
            CAN["CancelOrderCommandHandler"]
            GOI["GetOrderByIdQueryHandler"]
            GO["GetOrdersQueryHandler"]
            GOU["GetOrdersByUserQueryHandler"]
        end
        CPP["ICatalogPriceProvider<br/>price revalidation"]
    end

    subgraph DOM["AK.Order.Domain — no outward dependencies"]
        AGG["Order aggregate root<br/>_allowedTransitions state machine<br/>Pending → Confirmed → Paid / PaymentFailed"]
    end

    subgraph INFRA["AK.Order.Infrastructure — persistence & messaging"]
        UOW["UnitOfWork → OrderRepository<br/>IUnitOfWork · IOrderRepository"]
        CTX["OrderDbContext<br/>EF Core · Npgsql"]
        HCP["HttpCatalogPriceProvider → AK.Products"]
        OUT[("OutboxMessage table<br/>+ InboxState / OutboxState")]
        DRAIN["MassTransit bus outbox<br/>AddEntityFrameworkOutbox · UseBusOutbox"]
    end

    PG[("PostgreSQL · AKOrdersDb")]
    SB["Azure Service Bus<br/>integration-events topic"]

    EP --> MED --> VB
    VB --> CO & UOS & CAN & GOI & GO & GOU
    CO -->|revalidate price| CPP --> HCP
    CO --> AGG
    UOS --> AGG
    CAN --> AGG
    CO --> UOW
    GOI --> UOW
    GO --> UOW
    GOU --> UOW
    AGG --> UOW
    UOW --> CTX --> PG
    CTX --> OUT --> DRAIN --> SB

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;

    class EP,MED,VB,CO,UOS,CAN,GOI,GO,GOU,CPP,HCP,UOW,CTX,DRAIN service;
    class OUT,PG datastore;
    class SB paas;
```

One request enters through `OrderEndpoints` in the minimal-API host, which reads the caller's identity from the JWT and dispatches a command or query through `IMediator`. Every request first crosses the shared `ValidationBehavior` pipeline before reaching its slice — the `Features/` folder holds one vertical slice per use case (`CreateOrder`, `UpdateOrderStatus`, `CancelOrder`, `GetOrderById`, `GetOrders`, `GetOrdersByUser`). Write handlers act on the `Order` aggregate root, whose `_allowedTransitions` state machine is the only place an order's status can legally change; `CreateOrder` additionally revalidates every line against the catalogue through `ICatalogPriceProvider` before persisting. All persistence goes through `IUnitOfWork`/`OrderRepository` onto `OrderDbContext` (EF Core over Npgsql). The decisive detail is the outbox: the business row and the `OutboxMessage` are written in one PostgreSQL transaction, and MassTransit's bus outbox drains that table to Azure Service Bus afterwards — so the state change and its integration event can never diverge.

## Order saga

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant GW as AK.Gateway
    participant O as AK.Order
    participant P as AK.Products
    participant PAY as AK.Payments
    participant RZP as Razorpay
    participant NOT as AK.Notification

    C->>GW: POST /api/orders
    GW->>O: CreateOrderCommand
    Note over O: Order.Create() → status Pending<br/>(prices revalidated vs AK.Products catalogue)
    O-)P: OrderCreatedIntegrationEvent<br/>(EF outbox → Service Bus)
    O--)NOT: OrderCreatedNotification (Event Grid, fire-and-forget)

    P->>P: ReserveStockConsumer — decrement stock
    P-)O: StockReservedIntegrationEvent (direct publish)
    Note over O: OrderSaga: StockPending → Confirmed<br/>publishes OrderConfirmedIntegrationEvent (via outbox)
    O->>O: OrderConfirmedConsumer → status Confirmed
    O--)NOT: OrderConfirmedNotification (Event Grid)

    C->>GW: POST /api/payments/initiate
    GW->>PAY: InitiatePaymentCommand
    PAY->>RZP: CreateOrderAsync
    RZP-->>PAY: razorpay order id
    PAY-)O: PaymentInitiatedIntegrationEvent (EF outbox)
    PAY-->>C: razorpay order id + key id (widget opens)

    C->>RZP: pays in the Razorpay widget
    C->>GW: POST /api/payments/verify<br/>(order id, payment id, signature)
    GW->>PAY: VerifyPaymentCommand
    PAY->>RZP: VerifyPaymentSignature (HMAC-SHA256)
    Note over PAY: signature valid → payment.MarkSucceeded()
    PAY-)O: PaymentSucceededIntegrationEvent<br/>(EF outbox → Service Bus)
    PAY--)NOT: PaymentSucceededNotification (Event Grid)
    Note over O: PaymentSucceededConsumer → Confirmed → Paid<br/>ConfirmPayment() sets PaymentStatus = Paid

    Note over P,PAY: KI-005 — on the PaymentFailed branch the order moves to<br/>PaymentFailed but the reserved stock is NOT released (no compensation)
```

The saga is orchestrated by `OrderSaga` (a MassTransit state machine) and runs entirely over messages — no service calls another synchronously for the business flow. `AK.Order` never publishes an integration event directly from a handler: `OrderCreatedIntegrationEvent`, the saga's `OrderConfirmedIntegrationEvent`, and `AK.Payments`' `PaymentInitiatedIntegrationEvent`/`PaymentSucceededIntegrationEvent` are all written to an EF Core outbox in the same transaction as their state change and drained to Service Bus afterwards; only `AK.Products`' `StockReservedIntegrationEvent` is a direct publish, as that service has no outbox. Customer emails travel a deliberately separate path — fire-and-forget Event Grid notifications consumed by the serverless `AK.Notification` Functions — so a notification failure can never roll back the saga. The dashed note records **KI-005**: because there is no compensating stock-release step, a payment that fails after stock was reserved leaves that stock decremented.

The two eventing patterns this rests on — **domain events** (in-process) vs **integration events** (cross-service, via the outbox and Service Bus) — and the resilience posture (retry, circuit breaker, timeout) are the platform's cross-cutting design.

## How it was built

- Application patterns and cross-cutting design: [Event Bus design](../guides/eventbus-concepts.md) · [Resilience design](../guides/resilience-concepts.md).
- Per-service internals: the service technical design documents — [AK.Products](../../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) · [AK.Order](../../AK.Order/ORDER_TECHNICAL_DESIGN.md) · [AK.Payments](../../AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) · [AK.ShoppingCart](../../AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) · [AK.Discount](../../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) · [AK.Gateway](../../AK.Gateway/API_GATEWAY.md) · [AK.BuildingBlocks](../../AK.BuildingBlocks/BUILDING_BLOCKS.md). _(These describe the Phase-1 topology and carry a superseded banner; the patterns still hold.)_

## Decisions

- [ADR-001 — Microservices Architecture](../adr/ADR-001-microservices-architecture.md)
- [ADR-002 — Clean Architecture and Domain-Driven Design](../adr/ADR-002-clean-architecture-and-ddd.md)
- [ADR-004 — Polyglot Persistence](../adr/ADR-004-polyglot-persistence.md)
- [ADR-005 — SAGA Orchestration over 2PC and Choreography](../adr/ADR-005-saga-orchestration.md)
- [ADR-006 — Ocelot API Gateway over YARP](../adr/ADR-006-ocelot-api-gateway.md)
- [ADR-009 — Domain Events vs Integration Events](../adr/ADR-009-domain-events-vs-integration-events.md)
- [ADR-010 — CQRS and MediatR](../adr/ADR-010-CQRS-and-MediatR.md)
- [ADR-011 — Repository, Specification, and Unit of Work](../adr/ADR-011-Repository-Specification-and-Unit-of-Work.md)

## Open items

- [KI-005 — no stock-release compensation on payment failure](../KNOWN_ISSUES.md): when a payment fails, stock reserved by the saga is retained rather than released — a deliberate deferral pending a compensation workstream.
