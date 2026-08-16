# ADR-002: Clean Architecture and Domain-Driven Design

## Status
Accepted

---

## Context

Each AntKart microservice needs an internal structure that:

- Keeps business rules isolated from frameworks, databases, and transport — so that swapping a database or messaging provider does not require touching domain logic
- Is consistent enough that a developer familiar with one service can read any other
- Enables unit testing without starting a database, HTTP server, or message broker
- Separates *what the business does* (domain) from *how it is done* (infrastructure)

Three approaches were evaluated before adopting Clean Architecture with DDD tactical patterns.

---

## Options Considered

### Option 1: Transaction Script / Anemic Domain Model

Business logic lives entirely in service classes (`OrderService`, `PaymentService`). Domain objects are plain data bags — properties only, no behaviour. Services operate on them procedurally.

**Pros:** Simple to understand initially; no abstraction layer; familiar to developers from classic N-tier backgrounds.

**Cons:** Business rules scatter across service classes and duplicate wherever the same invariant applies. Validation must be manually enforced at every call site. Service classes grow into God objects as complexity increases. There is no natural home for domain events — they are either bolted on as side effects in services or omitted entirely. The approach works for simple CRUD; it fails under meaningful domain complexity.

### Option 2: Plain N-Tier (Presentation → Service → Repository)

Three layers: API → Service → Repository. The classic ASP.NET MVC structure.

**Pros:** Familiar pattern; works well for simple CRUD applications.

**Cons:** The Service layer still takes a direct dependency on EF Core entities or `DbContext`. Swapping the database typically requires changing service classes. Infrastructure details (EF annotations, connection tracking) leak upward. The boundary between "business logic" and "data access" is never enforced — over time, the service layer accumulates database concerns and the repository layer accumulates business rules.

### Option 3: Vertical Slice Architecture

Each feature is a complete slice from HTTP request to database, with no shared domain model. One folder per feature; adjacent features are entirely unrelated.

**Pros:** Per-feature cohesion; no shared abstraction overhead; a new developer only reads one folder to understand a feature.

**Cons:** Shared invariants must be duplicated across slices or pushed into the database. The Order bounded context in AntKart has a non-trivial state machine (`_allowedTransitions`), aggregate methods (`AddItem`, `UpdateStatus`, `Cancel`), and domain events — none of which belong cleanly inside a single feature slice. Vertical Slice is not wrong; it trades aggregate cohesion for feature cohesion, and that trade is only beneficial when the domain is genuinely simple CRUD.

**Note:** AK.Order uses Vertical Slice *organisation within the Application layer* (each feature is a self-contained folder: `Features/CreateOrder/`, `Features/GetOrderById/`, etc.) while still sharing a proper domain model. This combines the readability benefits of Vertical Slice with the invariant-enforcement benefits of a domain aggregate.

---

## Decision

Every AntKart microservice uses **Clean Architecture** as its layer structure, with **DDD tactical patterns** applied in services where the domain has meaningful behaviour beyond CRUD.

---

## The Four Layers

```
┌──────────────────────────────────────────────────────────┐
│                       API / Grpc                         │
│   HTTP endpoints, gRPC stubs, Program.cs, middleware     │
├──────────────────────────────────────────────────────────┤
│                     Infrastructure                       │
│   EF Core, MongoDB, Redis, SMTP, MassTransit consumers   │
├──────────────────────────────────────────────────────────┤
│                      Application                         │
│   Commands, Queries, Handlers, Validators, DTOs,         │
│   Repository/UnitOfWork interfaces, Mappers              │
├──────────────────────────────────────────────────────────┤
│                        Domain                            │
│   Entities, Value Objects, Domain Events, Enums,         │
│   Domain Exceptions, Specifications                      │
└──────────────────────────────────────────────────────────┘
         ↑ dependencies point inward only ↑
```

**The dependency rule:** every source code dependency points inward — toward higher-level policy. Domain knows nothing about any other layer. Application knows Domain. Infrastructure implements Application interfaces. API wires everything together.

Enforced by `ProjectReference` entries in every `.csproj`:

| Layer | References |
|-------|-----------|
| Domain | AK.BuildingBlocks only (Entity, IDomainEvent, ValueObject — pure contracts) |
| Application | Domain, AK.BuildingBlocks |
| Infrastructure | Application (Domain transitively) |
| API / Grpc | Application, Infrastructure, AK.BuildingBlocks |
| Tests | Application, Infrastructure, Domain — never API / Grpc |

---

## Why Clean Architecture — Demonstrated by Real Changes

The boundary discipline was not imposed theoretically — it has been tested by real changes made after the initial build:

| Change | Layers touched | Layers untouched |
|--------|---------------|-----------------|
| Keycloak → Microsoft Entra ID | `AddEntraIdAuthentication()` in BuildingBlocks, `appsettings.json` in each service, `Program.cs` auth wiring | All Domain layers, all Application handlers, all Infrastructure persistence, all consumers |
| RabbitMQ → Azure Service Bus | `MassTransitExtensions` transport config in BuildingBlocks, `Program.cs` configuration | All Domain layers, all MassTransit consumers (same `IConsumer<T>` interface), all command handlers |
| Local MongoDB → Cosmos DB (MongoDB API) | Connection string source (Key Vault instead of appsettings), `MongoClient` registration | Domain entities, Application handlers, all other Infrastructure classes — `MongoClient` code was unchanged |

If the dependency rule had been violated — if domain entities had imported EF Core attributes, or handlers had taken `DbContext` directly — these swaps would have required domain changes. They did not. The boundaries held under real-world pressure.

---

## DDD Tactical Patterns in AntKart

Twelve patterns are in active use across the codebase.

### 1. Aggregates and Aggregate Roots

An aggregate is a cluster of domain objects treated as one unit for data changes. The aggregate root is the only public entry point — external code holds references only to the root, never to internal child entities directly.

`IAggregateRoot` (BuildingBlocks) is the marker interface. The `Entity` base class provides identity, timestamps, and the domain event collection. In AK.Order:

```csharp
public sealed class Order : Entity, IAggregateRoot
{
    private readonly List<OrderItem> _items = [];

    public void AddItem(string productId, string productName, int quantity, decimal unitPrice)
    {
        var item = OrderItem.Create(Id, productId, productName, quantity, unitPrice);
        _items.Add(item);
        AddDomainEvent(new OrderItemAddedEvent(Id, productId, quantity));
    }

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (!_allowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException($"Cannot transition from {Status} to {newStatus}");
        Status = newStatus;
        SetUpdatedAt();
        AddDomainEvent(new OrderStatusChangedEvent(Id, Status, newStatus));
    }
}
```

The caller never manipulates `_items` or `Status` directly. The aggregate enforces its own invariants — invalid state transitions throw immediately, at the domain level, before any persistence occurs.

### 2. Value Objects

Immutable objects defined entirely by their attribute values, not by identity. Two value objects with identical attributes are equal.

`ValueObject` (BuildingBlocks) provides `GetEqualityComponents()` → structural equality via `SequenceEqual`. `ShippingAddress` in AK.Order:

```csharp
public sealed class ShippingAddress : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string PostalCode { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return PostalCode;
    }
}
```

AK.Products uses a C# `record` for `Money` — a deliberately different approach documented as an alternative. Both are valid: records have structural equality built in for simple cases; `ValueObject` provides more control over equality semantics for complex ones.

### 3. Domain Events

When something meaningful happens inside an aggregate, the entity records it as a domain event. Events are dispatched *after* `SaveChangesAsync` — guaranteeing that events are only raised for changes that were actually persisted to the database.

**Dispatch mechanism:**

```
Entity.AddDomainEvent(IDomainEvent)
    └─ appended to List<IDomainEvent> _domainEvents (private, on Entity base class)

UnitOfWork.SaveChangesAsync()
    1. await DbContext.SaveChangesAsync()            ← persist the business change
    2. collect all entities with pending DomainEvents from ChangeTracker
    3. foreach domainEvent → IPublishEndpoint.Publish(domainEvent)  ← dispatch to MassTransit
    4. entity.ClearDomainEvents()                   ← prevent replay on next save cycle
```

This sequencing guarantees two properties: an event is never published for a change that was not saved; and no event is silently lost because the publish was called before the save completed. The Outbox pattern (§8 below) adds transactional delivery guarantees on top of this.

Domain events in AntKart:

| Service | Domain events |
|---------|-------------|
| AK.Order | `OrderCreatedEvent`, `OrderStatusChangedEvent`, `OrderCancelledEvent` |
| AK.Payments | `PaymentCreatedEvent`, `PaymentSucceededEvent`, `PaymentFailedEvent` |

### 4. Repository Pattern

`IRepository<T>` interfaces live in the Application layer. Infrastructure provides the implementations. Application handlers depend only on the interface — never on EF Core, `DbContext`, or MongoDB.Driver directly.

```csharp
// Application — what the handler needs to know
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
}

// Infrastructure — the EF Core implementation (internal, invisible to Application)
internal sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Order order, CancellationToken ct) =>
        await db.Orders.AddAsync(order, ct);
}
```

Tests mock `IOrderRepository` with `Mock<IOrderRepository>` — no database required, no EF Core in-memory needed.

### 5. Unit of Work

`IUnitOfWork` coordinates multiple repositories in a single database transaction. One call to `SaveChangesAsync` commits all pending changes atomically and triggers domain event dispatch. This prevents partial saves (order saved but items not) and ensures events are never dispatched for uncommitted data.

### 6. Specification Pattern

Used in AK.Products and AK.Order to express query criteria as named, composable objects rather than embedding filter logic inside repository methods.

```csharp
// A reusable, testable query criterion
public sealed class ProductsByCategorySpec : BaseSpecification<Product>
{
    public ProductsByCategorySpec(string category)
        : base(p => p.CategoryName == category) { }
}

// Repository accepts a specification — no SQL or LINQ in the Application layer
var products = await repository.ListAsync(new ProductsByCategorySpec("Women"), ct);
```

Specifications can be unit-tested without a database by checking their criteria expression directly. Complex combinations (by category + price range + availability) compose without modifying the repository.

### 7. CQRS and MediatR

Every state change is a `Command`; every data read is a `Query`. MediatR dispatches both. `ValidationBehavior<TRequest, TResponse>` (BuildingBlocks) runs FluentValidation automatically before every handler — handlers receive only valid, non-null input. See ADR-007 for complete implementation detail.

### 8. Outbox Pattern

AK.Order and AK.Payments use the MassTransit EF Core outbox. Integration events are written to the same database transaction as the business change. A MassTransit background relay reads and delivers them asynchronously.

This solves the dual-write problem: without the outbox, a crash between `SaveChangesAsync` and `Publish()` silently drops the integration event — the order is saved but downstream services never learn of it. With the outbox, the event either commits with the business data or both roll back together. See ADR-006 for the messaging context.

### 9. SAGA

AK.Order contains a MassTransit state machine (`OrderSaga`) that orchestrates the multi-step Order → Stock Reservation → Payment flow across three independent services. The SAGA reacts to integration events, maintains its state durably in PostgreSQL (`OrderSagaState`), and triggers compensation (cancel the order, release stock) if any step fails. See ADR-002 for complete detail.

### 10. Result\<T\> Pattern

`Result<T>` (BuildingBlocks) is used in command handlers where the failure path is an expected business outcome that the caller needs to distinguish from an exceptional condition.

```csharp
// AK.Order — CancelOrderCommandHandler
if (order.Status is OrderStatus.Delivered)
    return Result<bool>.Failure("A delivered order cannot be cancelled");

if (order.Status is OrderStatus.Cancelled)
    return Result<bool>.Failure("Order is already cancelled");

// Endpoint maps Result.IsSuccess → 204, Result.Failure → 409 with the message
```

This is not applied universally. `CreateOrderCommandHandler` uses exceptions — an order failing to create due to bad input is genuinely exceptional. `CancelOrderCommandHandler` and `UpdateOrderStatusCommandHandler` use `Result<T>` because "already cancelled" and "invalid status transition" are valid business outcomes, not programming errors. The code explicitly documents both approaches as a comparison.

### 11. Manual Mappers

Each service has `*Mapper.cs` static extension method classes (e.g., `OrderMapper.cs`, `ProductMapper.cs`) that map domain entities to DTOs.

This is a deliberate architectural choice:

- **Compile-time safety:** mapping errors — a renamed property, a new required field on the DTO — are caught at build time, not when the first production request hits a null or missing property
- **No reflection overhead:** AutoMapper and Mapster resolve mappings at runtime via reflection or source generation at startup; manual mappers compile to direct property assignments with no runtime cost
- **Debuggable:** stepping through a mapping in a debugger shows exactly which property is being set and where — no framework stack frames to step over
- **Explicit contracts:** adding a new field to a DTO requires a visible, intentional change in the mapper; nothing is silently omitted or included by convention

The cost is verbosity — 10–20 lines of straightforward property assignment per entity. The benefit is that every field in every response is explicitly accounted for in code.

### 12. Minimal API Endpoints

The API layer uses .NET Minimal APIs rather than MVC controllers. Each service has one or more `*Endpoints.cs` static classes with a `Map*Endpoints(WebApplication app)` method called from `Program.cs`.

```csharp
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization("authenticated");

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderByIdQuery(id, http.GetUserId()));
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/", async (CreateOrderDto body, HttpContext http, ISender sender) =>
        {
            var cmd = new CreateOrderCommand(
                http.GetUserId(), http.GetUserEmail(), http.GetUserDisplayName(), body.Items);
            var order = await sender.Send(cmd);
            return Results.Created($"/api/orders/{order.Id}", order);
        });
    }
}
```

No `[ApiController]`, no `ControllerBase`, no action filters. The endpoint extracts JWT claims from `HttpContext`, builds the CQRS command or query, calls `sender.Send()`, and maps the result to an HTTP status and body. All business logic is in the handler — the endpoint is a thin adapter between HTTP and the application layer.

---

## Strategic DDD: Bounded Contexts

AntKart's service boundaries map directly to DDD bounded contexts. Each context has its own ubiquitous language — the word "product" in the Catalogue means a full product document with pricing and stock; in the Order context it means a snapshot of a line item at the time of purchase.

| Bounded context | Service | Key aggregate |
|----------------|---------|--------------|
| Product Catalogue | AK.Products | `Product` (Category, Stock) |
| Shopping | AK.ShoppingCart | `Cart` (CartItem) |
| Ordering | AK.Order | `Order` (OrderItem, state machine) |
| Payment | AK.Payments | `Payment` (SavedCard, Razorpay token) |
| Identity | AK.UserIdentity | External — Microsoft Entra ID |
| Notification | AK.Notification | `Notification` (Channel, Template) |

**Context mapping:** services never share a database. Data that crosses a bounded context boundary is denormalised — the product name, SKU, and unit price are copied into the `OrderItem` at order creation time. The `OrderCreatedIntegrationEvent` carries the snapshot; the consuming service stores what it needs without querying the source service. This makes each service read-independent and resilient to the unavailability of other services.

---

## Consequences

**Easier:**
- Any command or query handler can be unit-tested with `new Handler(mockRepository)` — no HTTP, no database, no message broker required
- Infrastructure can be replaced without touching domain or application code — demonstrated in production by Keycloak → Entra ID, RabbitMQ → Service Bus, and local MongoDB → Cosmos DB
- Validation is automatic via the MediatR pipeline; handlers always receive structurally valid input
- A developer who knows AK.Order immediately understands AK.Payments — same folder layout, same pattern names, same conventions
- Error handling is centralised in `ExceptionHandlerMiddleware` — handlers throw domain exceptions, the middleware maps them to HTTP status codes

**Harder:**
- More files per feature — command, handler, validator, and mapper are separate files; this is intentional friction that keeps each file single-purpose
- New contributors must learn the layering convention before knowing where to put code — the answer to "where does this go?" is specific and must be learned
- The Repository + UnitOfWork abstraction over EF Core is occasionally verbose for queries that EF Core would express in one line; consistency was chosen over brevity
- Domain events + outbox + SAGA together add meaningful complexity to what is ultimately a write operation; this is appropriate for the Order bounded context but deliberately absent from AK.Discount, which is simple CRUD and uses a simpler approach
