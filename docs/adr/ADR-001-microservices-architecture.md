# ADR-001: Microservices Architecture

## Status
Accepted

---

## Context

AntKart needed an application architecture that could model an e-commerce domain with genuinely distinct bounded contexts (catalogue, cart, orders, payments, identity, notifications), serve as a learning platform for the full cloud-native .NET stack, and demonstrate independently deployable services at every layer from code to infrastructure.

Three architectural patterns were evaluated honestly before committing to microservices.

---

## Options Considered

### Option 1: Monolith

A single deployable unit — one process, one database, one release pipeline.

**Pros:**
- Simplest to build and deploy at the start
- No network calls between components; no distributed tracing needed
- Transactions span the whole domain — no eventual consistency to design
- Straightforward to test end-to-end; no inter-service harness required

**Cons:**
- All modules share one codebase and one deploy pipeline — a bug in notifications blocks a release for orders
- A single shared database schema couples every feature together; schema migrations must coordinate across all teams
- Scaling the whole application to serve load on one feature (cart during a flash sale) is wasteful
- Technology lock-in: the entire codebase must share the same language, framework, and runtime version

### Option 2: Modular Monolith

One deployable unit, but with strict internal module boundaries enforced at compile time — separate assemblies per domain, no direct cross-module references, integration via in-process interfaces.

**Pros:**
- Simpler deployment than microservices — one process, no network between modules
- Strong compile-enforced boundaries without distributed systems complexity
- Shared transactions still possible within the process
- Can be extracted into microservices later when the seams are understood from production load

**Cons:**
- All modules deploy together — a bug in any module requires redeploying the whole application
- A memory leak or unhandled exception in one module can crash the entire process
- Technology heterogeneity per module is still constrained by the shared process

**This is the honest default for most teams.** For a new product where the domain is not yet well understood, a modular monolith is the better starting point. Netflix, Shopify, and Amazon all began as monoliths. Decomposing early — before production usage reveals where the real seams are — almost always produces microservices that are the wrong granularity: either too fine (network hops for trivially related data) or too coarse (effectively separate monoliths). The overhead of operating a distributed system is substantial, and taking it on before the domain boundaries have been proven by usage is a high-risk bet.

### Option 3: Microservices

Independently deployable services, each owning its own data store and communicating via async messaging (Azure Service Bus) and synchronous HTTP/gRPC.

**Pros:**
- Each service deploys independently — shipping a notification fix never touches the order service
- Technology heterogeneity per service: MongoDB for the product catalogue, Redis for the cart, PostgreSQL for orders and payments — each chosen for its access pattern
- Independent scaling — cart and product services scale horizontally without affecting payments
- Fault isolation — a failure in the notification service does not prevent orders from being placed
- Team independence — each service has a clear owner with its own release cadence

**Cons:**
- Distributed systems complexity: every network call can fail, time out, or return stale data
- Eventual consistency: what was one database transaction becomes an event-and-consumer pair that can fail mid-way, requiring compensation
- Operational overhead: 8 services × (container, database, health check, logs, metrics, distributed traces) = significant infrastructure cost
- Testing: integration across services requires a careful harness; end-to-end tests are slow and fragile
- Debugging: a single user action may span 4 services and 3 async hops, making root cause analysis harder

---

## Decision

AntKart uses **microservices** — one independently deployable service per bounded context.

This was chosen over a modular monolith for two honest reasons:

1. **Portfolio platform, not a new product.** AntKart deliberately demonstrates the full cloud-native stack: async messaging, SAGA orchestration, API gateway, EF Core outbox, distributed tracing, Infrastructure-as-Code, workload identity, and polyglot persistence. These cannot be demonstrated in a single-process architecture. The cost of the distributed complexity is justified because the complexity itself is the subject of study.

2. **Genuine domain independence.** The bounded contexts have materially different data stores, scaling profiles, and external integrations that would make a shared process awkward even in a production setting: the Product catalogue runs on Cosmos DB (MongoDB API) for flexible document schema and global scale; the Cart runs on Redis for sub-millisecond ephemeral reads; Discount uses SQLite for simple CRUD with no external dependency; Payments integrates with Razorpay and requires ACID guarantees; Notifications send email via SMTP on a completely separate write path. The database-per-service boundary is real and not invented for the exercise.

For a production product team building something new: start with a modular monolith. Let production usage reveal the seams. Extract microservices from the modules where independent deployment, scaling, or team autonomy is actually needed — not from a diagram drawn before the first user has been served.

---

## 12-Factor App Principles

AntKart applies all twelve factors:

| Factor | AntKart implementation |
|--------|----------------------|
| **I. Codebase** | Single Git repo (`AntKart/`) — one codebase tracked in version control, many deploys (dev, staging, prod) |
| **II. Dependencies** | All dependencies declared in `.csproj` files; `nuget.config` at the repo root clears the default feed list and pins `nuget.org` only; no implicit system-wide packages |
| **III. Config** | No credentials in source code; `appsettings.json` holds only non-secret references (Key Vault URIs, Service Bus namespace names); secrets are environment variables injected at runtime via Docker Compose env files or AKS workload identity |
| **IV. Backing services** | PostgreSQL, Redis, Cosmos DB, Azure Service Bus, and SMTP are attached resources addressed by connection details from configuration, not hardcoded; swapping Docker Compose (dev) for Azure (prod) requires only an environment variable change |
| **V. Build, release, run** | GitHub Actions CI/CD builds a container image (build), tags it with the Git SHA (release), and deploys to AKS (run); images are immutable — the same image runs in staging and production |
| **VI. Processes** | Each service is stateless; no in-memory session state; cart lives in Redis, not the service process; any container replica can serve any request |
| **VII. Port binding** | Each service exports HTTP on a declared port; services do not share port assignments |
| **VIII. Concurrency** | Scale by adding container replicas; stateless design means any number of replicas serve requests without coordination |
| **IX. Disposability** | Services start in seconds (no warm-up database seeding in production); graceful shutdown drains in-flight requests; SMTP, database, and Service Bus connections are properly disposed per-request or per-scope |
| **X. Dev/prod parity** | The same container images run locally — against live cloud services or via cloud port-forwarding — as in AKS; seed data runs via the same code path; EF Core migrations apply via the same `dotnet ef database update` command |
| **XI. Logs** | Serilog writes structured JSON to stdout (the console); Azure Monitor collects it — the AKS OMS agent ships container stdout into Log Analytics (the `ContainerLog` table) where it is queried with KQL; no log files inside containers, no ELK stack |
| **XII. Admin processes** | EF Core migrations run as one-off `dotnet ef database update` commands, not embedded in service startup (AK.Notification auto-migrates on startup — a deliberate trade for operational simplicity in a background-service context) |

---

## Cloud-Native Pillars

| Pillar | How AntKart implements it |
|--------|--------------------------|
| **Containerised** | Every service has a Dockerfile at its API/Grpc project root; images run locally against live cloud services (or via cloud port-forwarding); production targets Azure Kubernetes Service (AKS) |
| **Dynamically orchestrated** | AKS manages scheduling, scaling, and health-based pod restarts; Terraform provisions the cluster and all supporting infrastructure |
| **Microservices-based** | 8 independently deployable services, each owning its data store, with communication via Azure Service Bus (async) and Ocelot (synchronous HTTP routing) |
| **DevOps pipeline** | GitHub Actions CI/CD: build → test → push to Azure Container Registry → deploy to AKS; all infrastructure managed by Terraform + Terragrunt; no manual steps in production |

---

## Addressing the Trade-offs

| Trade-off | AntKart response |
|-----------|-----------------|
| **Distributed complexity** | MassTransit abstracts transport — the same consumer code runs against the in-memory test harness, RabbitMQ (early dev), and Azure Service Bus (production). `MassTransitExtensions.AddServiceBusMassTransit()` in BuildingBlocks encapsulates all wiring; swapping transport is a configuration change |
| **Eventual consistency** | The Order → Stock Reservation → Payment flow is orchestrated by a MassTransit state machine SAGA (`OrderSaga` in AK.Order). The SAGA reacts to integration events, maintains durable state in PostgreSQL, and triggers compensating transactions (order cancellation) if any step fails. See ADR-002 |
| **Partial failure / dual write** | The MassTransit EF Core outbox pattern (AK.Order, AK.Payments) writes integration events atomically with business data in the same database transaction. A background relay delivers them. No event is lost if the service crashes between the DB write and the publish. See ADR-006 |
| **Operational overhead** | Terraform + Terragrunt provision all Azure infrastructure as code; one `terragrunt run-all apply` builds the full environment. Services run locally against live cloud services (or via cloud port-forwarding) with no manual steps. Serilog + Azure Monitor (Log Analytics) provide centralised logging across all services |
| **Testing** | Unit tests mock at interface boundaries (no DB, no HTTP). `AK.IntegrationTests` uses the MassTransit in-memory test harness — 35 integration tests cover SAGA flows and notification consumers without RabbitMQ or Azure Service Bus |
| **Service discovery** | In Docker Compose, service names resolve via Docker's embedded DNS. In AKS, Kubernetes Services provide stable cluster-internal DNS. The Ocelot API Gateway is the single client-facing entry point, routing to all REST services |

---

## Service Boundaries

| Service | Bounded context | Dev port | Database |
|---------|----------------|----------|----------|
| AK.Products | Product catalogue | 5077 | Cosmos DB (MongoDB API) |
| AK.Discount | Coupon management | 5001 (gRPC) | SQLite |
| AK.ShoppingCart | Cart session | 5079 | Redis |
| AK.Order | Order lifecycle | 5080 | PostgreSQL |
| AK.UserIdentity | Authentication / identity | 5085 | Microsoft Entra ID (external) |
| AK.Gateway | API routing | 8000 | — |
| AK.Payments | Payment processing | 5086 | PostgreSQL |
| AK.Notification | Transactional email | 5087 | PostgreSQL |

---

## Consequences

**Easier:**
- Each service deploys, scales, and fails independently — a Redis outage affects only the cart, not payments
- Technology is chosen per bounded context — MongoDB for the catalogue, Redis for the cart, PostgreSQL for financial data
- Each service can be developed, tested, and released without coordinating with other services
- New services are added by following the documented checklist without touching existing services

**Harder:**
- Every cross-service interaction is a distributed systems problem — latency, retries, idempotency, and eventual consistency must be designed explicitly
- The test surface for end-to-end scenarios is larger and harder to keep deterministic
- Debugging a failed user action requires correlating logs across multiple services by `X-Correlation-Id`
- Infrastructure cost is proportionally higher — 8 databases, 8 health checks, 8 container images, unified log aggregation
