# Architecture Decision Records

An Architecture Decision Record (ADR) captures a single significant architectural or platform decision — its context, the options considered, the decision taken, and its consequences.

## Foundational

- [ADR-001 — Microservices Architecture](ADR-001-microservices-architecture.md) — Independently deployable .NET services over a monolith, each owning its data and deployment lifecycle.
- [ADR-002 — Clean Architecture and Domain-Driven Design](ADR-002-clean-architecture-and-ddd.md) — Layered Domain / Application / Infrastructure / API design with inward dependencies and a rich domain model.
- [ADR-003 — Fault Tolerance with Polly](ADR-003-fault-tolerance-with-polly.md) — Retry, circuit breaker, and timeout pipelines on outbound calls, with graceful degradation.

## Application Patterns

- [ADR-004 — Polyglot Persistence](ADR-004-polyglot-persistence.md) — One database per service, each chosen to fit its workload.
- [ADR-005 — SAGA Orchestration](ADR-005-saga-orchestration.md) — An orchestrated SAGA state machine over two-phase commit and pure choreography for the order workflow.
- [ADR-006 — Ocelot API Gateway](ADR-006-ocelot-api-gateway.md) — Ocelot as the in-process gateway for routing, authentication, rate limiting, and quality of service.
- [ADR-007 — MassTransit over Raw RabbitMQ](ADR-007-masstransit-over-raw-rabbitmq.md) — MassTransit for SAGA, outbox, retry, and consumer pipelines instead of the raw broker client.
- [ADR-008 — Shared DDD Contracts in BuildingBlocks](ADR-008-shared-ddd-contracts-in-buildingblocks.md) — Common DDD base types and contracts centralised in the shared building-blocks library.
- [ADR-009 — Domain Events vs Integration Events](ADR-009-domain-events-vs-integration-events.md) — Two distinct event patterns: in-process domain events and cross-service integration events.
- [ADR-010 — CQRS and MediatR](ADR-010-CQRS-and-MediatR.md) — Command/query separation via MediatR with a validation pipeline behaviour.
- [ADR-011 — Repository, Specification, and Unit of Work](ADR-011-Repository-Specification-and-Unit-of-Work.md) — Persistence abstractions built on the Repository, Specification, and Unit of Work patterns.

## Cloud & DevOps Design

- [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](ADR-012-iac-with-terraform-terragrunt.md) — All cloud infrastructure defined as code, composed and kept DRY with Terragrunt.
- [ADR-013 — Key Vault RBAC and Observability Foundation](ADR-013-key-vault-rbac-and-observability-foundation.md) — Key Vault RBAC authorization, workspace-based application insights, and a container-registry tiering strategy.
- [ADR-014 — Cosmos DB and Service Bus](ADR-014-cosmosdb-and-servicebus.md) — Managed document database and message broker as the cloud data and messaging backbones.
- [ADR-015 — Messaging Migration to Service Bus](ADR-015-messaging-migration-to-service-bus.md) — Migrate messaging to a managed service bus with token-based authentication.
- [ADR-016 — Data Migration to Cosmos DB and Workload Identity](ADR-016-data-migration-cosmosdb-and-workload-identity.md) — Move product persistence to the managed document database and establish the workload-identity foundation.
- [ADR-017 — Identity Provider, Functions, and Event Routing](ADR-017-entra-id-functions-eventgrid.md) — Managed identity provider, isolated-worker functions, and event-routing topology.
- [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](ADR-018-aks-workload-identity-base-image.md) — A managed Kubernetes cluster with workload identity and a custom hardened base image.
- [ADR-019 — Serverless Notification with Functions and Event Routing](ADR-019-serverless-notification-functions-eventgrid.md) — Notification as consumption-plan functions, with a clear messaging-versus-eventing transport boundary.
- [ADR-020 — API Management as the Managed Edge Gateway](ADR-020-api-management-managed-edge-gateway.md) — A managed API gateway as the external edge in front of internal cluster routing.
- [ADR-021 — Retire the Dedicated Identity Service in Favour of Microsoft Entra ID](ADR-021-retire-identity-service-for-entra.md) — Delegate authentication to Entra and remove the application-hosted identity service; user and role administration move to Entra/Graph.
- [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials to Azure](ADR-022-cicd-github-actions-oidc.md) — Continuous integration and delivery on GitHub Actions, authenticating to Azure with OIDC workload identity federation and no stored credentials.
- [ADR-023 — CI/CD Pipeline Design and Repository Strategy](ADR-023-cicd-pipeline-design-and-repository-strategy.md) — Per-service path-filtered pipelines; two decoupled workflows per service (CI on PR, CD on merge); GitOps delivery via a Git image-tag update reconciled by Argo CD; shared library chart reused per service; polyrepo-ready layout.
- [ADR-024 — CD GitOps Write Contention on the Tag-Bump Push](ADR-024-cd-gitops-write-contention.md) — Six CD workflows racing to push tag bumps to master left services on stale images; fix with rebase-and-retry on the push (interim), rejecting a shared concurrency group (silent cancellation) and deferring a separate config repository (delivery-model change).
- [ADR-025 — Observability Architecture: Serilog for Logs, OpenTelemetry for Traces (metrics portion superseded)](ADR-025-observability-architecture.md) — Serilog JSON to Log Analytics `ContainerLog` and OpenTelemetry traces to Application Insights (OTel log export left off to avoid double ingest; OTel pinned to 1.13.x on net9). The metrics half — Prometheus `/metrics` and the self-hosted Prometheus/Grafana stack — was implemented and then **deliberately removed** in favour of a managed platform (Datadog, under evaluation); recorded as a Superseded decision.
