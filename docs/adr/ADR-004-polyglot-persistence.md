# ADR-004: Polyglot Persistence — One Database Per Service

## Status
Accepted

## Context
Microservices need independent data stores. A single shared database creates tight coupling between services: schema changes in one service risk breaking others, deployment of any service requires coordinating with a shared database migration, and each service cannot be scaled independently. The shared-database pattern is the most common mistake when moving from a monolith to microservices.

## Decision
Each service owns its database engine, chosen for its data access pattern:

| Service | Database | Reason |
|---------|----------|--------|
| AK.Products | MongoDB | Flexible document schema for varied product attributes (sizes, colors, specs differ by category). Read-heavy catalogue — MongoDB's horizontal scaling and secondary indexes suit this well |
| AK.Order | PostgreSQL | ACID transactions for financial records. Relational joins for order + order items. EF Core migrations for schema evolution |
| AK.Payments | PostgreSQL | Financial compliance requires ACID guarantees and a full audit trail of every payment state transition |
| AK.ShoppingCart | Redis | Ephemeral session data, sub-millisecond reads, 30-day TTL for cart expiry, no durability requirement |
| AK.Discount | SQLite | Simple CRUD for coupon codes. Self-contained with no external dependency. Volumes-mounted for persistence in Docker |
| AK.Notification | PostgreSQL | Delivery tracking and 90-day audit log of all outbound notifications |

## Consequences
**Easier:** Each service can be scaled independently (Redis for cart, read replicas for Products). Schema changes in one service don't affect others. Each team/developer can choose the right tool.

**Harder:** No cross-service SQL joins — data that needs to cross a boundary must be denormalised (product name, customer name, order number are copied into Payment and Notification records at creation time). Eventual consistency must be designed explicitly via integration events. Each service's database requires its own backup/restore strategy.
