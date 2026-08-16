# ADR-008: Shared DDD Contracts in AK.BuildingBlocks

## Status
Accepted

## Context
When the first four services were built independently, each defined its own versions of the core DDD contracts: `Entity` base class, `IDomainEvent`, and `IAggregateRoot`. By the time AK.Notification was added there were five different implementations with inconsistent patterns:

- **ID types:** Products used `string` (MongoDB), the rest used `Guid` — but the string generation logic differed
- **Timestamp types:** Some used `DateTime`, others `DateTimeOffset` — mixing these causes timezone bugs
- **Nullability:** `UpdatedAt` was non-nullable in Payments (set to `DateTimeOffset.MinValue` at creation) and nullable in Order — inconsistent semantics
- **Domain event list:** Three different implementations of `AddDomainEvent()` / `ClearDomainEvents()`

This made cross-service code reviews confusing and prevented reuse of any tooling built around these contracts.

## Decision
Move shared DDD contracts to `AK.BuildingBlocks/DDD/`:

| Class/Interface | Used by | Key detail |
|----------------|---------|------------|
| `Entity` | Order, Payments, Notification | `Guid Id`, `DateTimeOffset? UpdatedAt` (null until first mutation) |
| `StringEntity` | Products | `string Id = Guid.NewGuid().ToString("N")` — 32-char hex, MongoDB BSON string |
| `IDomainEvent` | All | Marker interface for domain event records |
| `IAggregateRoot` | All | Marker interface for aggregate root entities |
| `ValueObject` | Order (ShippingAddress) | `GetEqualityComponents()` → structural equality |

Each Domain layer project adds a `ProjectReference` to `AK.BuildingBlocks`. This is the only BuildingBlocks dependency at the Domain level — no infrastructure packages enter the domain.

## Consequences
**Easier:** One place to fix timestamp bugs, nullability, or domain event dispatch. New services immediately have consistent patterns. `DateTimeOffset` everywhere prevents timezone ambiguity.

**Harder:** Domain projects now depend on BuildingBlocks — this is acceptable because BuildingBlocks contains only contracts (no infrastructure code). AK.Discount (gRPC, SQLite int Id) does not use these contracts and remains independent.
