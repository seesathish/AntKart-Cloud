# AntKart Platform Roadmap

## Purpose and how to use this document

This is the single authoritative record of what the AntKart platform has **delivered**, what is **in progress**, and what is **planned**. It exists so that any reader can understand the full state of the platform from one file and then follow links into the detailed guides and Architecture Decision Records for depth. It summarises and links; it does not restate detail that already lives in the guides. Status is expressed only as **Delivered**, **In progress**, or **Planned**.

**Start here:** the [Development Guide](../DevelopmentGuide.md) is the spine of the documentation (each layer with its diagrams, build guides, and governing ADRs); the [C4 renders](C4Renders/) — authored in Structurizr — are the image source of truth; the [AKS Guide](guides/aks-guide.md) covers containerization, the cluster, and workload identity.

## Platform at a glance

AntKart is a cloud-native microservices e-commerce platform, built as a reference implementation to demonstrate enterprise cloud-native practice end to end — Clean Architecture, an event-driven SAGA, infrastructure as code, a secret-less identity model, and a managed Kubernetes runtime.

It comprises **six deployable services** plus a **serverless notifications** application:

| Component | Transport | Primary store |
|-----------|-----------|---------------|
| [AK.Products](../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) | REST (Minimal API) | Cosmos DB (MongoDB API) |
| [AK.Discount](../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) | gRPC | PostgreSQL |
| [AK.ShoppingCart](../AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) | REST (Minimal API) | Redis |
| [AK.Order](../AK.Order/ORDER_TECHNICAL_DESIGN.md) | REST (Minimal API) | PostgreSQL + SAGA |
| [AK.Payments](../AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) | REST (Minimal API) | PostgreSQL + Razorpay |
| [AK.Gateway](../AK.Gateway/API_GATEWAY.md) | Ocelot API gateway | — |
| [AK.Notification](guides/cloud-migration-guide.md) | Serverless (Event Grid → Azure Functions) | PostgreSQL (history) + ACS Email |

Core Azure services in use: **Microsoft Entra ID**, **Azure Kubernetes Service**, **Azure Container Registry**, **Azure Cosmos DB**, **Azure Database for PostgreSQL Flexible Server**, **Azure Managed Redis**, **Azure Service Bus**, **Azure Event Grid**, **Azure Functions**, **Azure Communication Services**, **Azure Key Vault**, and **Azure Monitor / Log Analytics**.

## Delivered

**Microservices foundation**
- Independently deployable microservices, each owning its data and deployment lifecycle — [ADR-001](adr/ADR-001-microservices-architecture.md)
- Clean Architecture and Domain-Driven Design per service — [ADR-002](adr/ADR-002-clean-architecture-and-ddd.md)
- CQRS with a mediator pipeline and validation behaviour — [ADR-010](adr/ADR-010-CQRS-and-MediatR.md)
- Repository, Specification, and Unit of Work persistence abstractions — [ADR-011](adr/ADR-011-Repository-Specification-and-Unit-of-Work.md)
- Orchestrated SAGA with a transactional outbox for at-least-once, dual-write-safe eventing — [ADR-005](adr/ADR-005-saga-orchestration.md) · [Event Bus design](guides/eventbus-concepts.md)
- Domain events (in-process) and integration events (cross-service) as two distinct patterns — [ADR-009](adr/ADR-009-domain-events-vs-integration-events.md)
- In-cluster API gateway (Ocelot) — routing, JWT passthrough, per-route rate limiting and QoS; the internal gateway in the two-gateway edge model — [ADR-006](adr/ADR-006-ocelot-api-gateway.md)
- gRPC for synchronous service-to-service calls (the discount lookup) — [AK.Discount design](../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md)
- Polly-based resilience (retry, circuit breaker, timeout) on outbound dependencies — [ADR-003](adr/ADR-003-fault-tolerance-with-polly.md) · [Resilience design](guides/resilience-concepts.md)
- Polyglot persistence — one store per service, each chosen to fit its workload — [ADR-004](adr/ADR-004-polyglot-persistence.md)
- Shared cross-cutting library (DDD base types, auth, messaging, resilience, middleware) — [ADR-008](adr/ADR-008-shared-ddd-contracts-in-buildingblocks.md) · [Building Blocks](../AK.BuildingBlocks/BUILDING_BLOCKS.md)

**Identity**
- Microsoft Entra ID as the identity provider; each service validates tokens and authorizes from the flat `roles` claim; the application identity service is retired — [ADR-021](adr/ADR-021-retire-identity-service-for-entra.md) · [ADR-017](adr/ADR-017-entra-id-functions-eventgrid.md)
- OAuth2 Authorization Code with PKCE for interactive (public) clients, delegated user tokens, app roles, and the `email` optional claim used for user-derived notification recipients — [OAuth2 + PKCE concepts](guides/oauth2-pkce-concepts.md) · [Cloud Migration Guide](guides/cloud-migration-guide.md)

**Cloud data and messaging**
- Product catalogue on Azure Cosmos DB (MongoDB API), connection string sourced from Key Vault — [ADR-016](adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [Cosmos DB concepts](guides/cosmosdb-concepts.md)
- Azure Database for PostgreSQL Flexible Server and Azure Managed Redis provisioned as code — [Infrastructure Guide](guides/infrastructure-guide.md) · [infrastructure/README](../infrastructure/README.md)
- Messaging on Azure Service Bus with MassTransit, over Entra token auth against IaC-owned topology — [ADR-014](adr/ADR-014-cosmosdb-and-servicebus.md) (Cosmos + Service Bus provisioning) · [ADR-015](adr/ADR-015-messaging-migration-to-service-bus.md) · [ADR-007](adr/ADR-007-masstransit-over-raw-rabbitmq.md) · [Messaging concepts](guides/messaging-concepts.md)

**Serverless notifications**
- Event Grid custom topic → Azure Functions → Azure Communication Services email, proven end to end, with managed-identity publishing and sending — [ADR-019](adr/ADR-019-serverless-notification-functions-eventgrid.md) · [Serverless & Eventing concepts](guides/serverless-eventing-concepts.md) · [Cloud Migration Guide](guides/cloud-migration-guide.md)

**Order pricing integrity**
- Server-authoritative pricing: the catalogue is the source of truth, client-submitted prices are advisory, only a price increase interrupts the customer (409), a missing/inactive product returns 422, and an unreachable catalogue fails closed (503) — [AK.Order design](../AK.Order/ORDER_TECHNICAL_DESIGN.md)

**Containerization**
- Multi-stage, non-root images (port 8080) for all six services, a repository-root `.dockerignore`, and images published to the Azure Container Registry — [AKS Guide](guides/aks-guide.md#container-strategy) · [Container Configuration](guides/container-configuration.md)

**Kubernetes**
- AKS cluster `aks-antkart-dev` provisioned by Terraform — Azure CNI Overlay, OIDC issuer and workload identity enabled at creation, Azure RBAC, OMS agent, and kubelet AcrPull for credential-free image pulls — [AKS Guide](guides/aks-guide.md#the-aks-cluster) · [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md)

**Secret-less workload identity**
- One user-assigned managed identity per service with a federated credential trusting the cluster OIDC issuer, and a least-privilege role matrix; verified reading Key Vault from a pod with no stored secret — [AKS Guide](guides/aks-guide.md#workload-identity) · [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md)

**Kubernetes deployment (Helm)**
- All six services run on AKS via Helm — a single generic chart instantiated per service, with workload-identity ServiceAccounts, a startupProbe gating liveness (`/health/live`) and readiness (`/health/ready`) so Key-Vault-at-boot never restart-loops a pod, resource requests sized to the node pool, and ClusterIP services on 8080 (AK.Discount uses TCP probes for its h2c gRPC port) — [Helm charts](../deploy/helm/README.md) · [AKS Guide](guides/aks-guide.md#deploying-the-services-helm)

**Ingress and TLS**
- Public HTTPS entry point via self-managed ingress-nginx exposing the **gateway only**, with cert-manager automated TLS. Cut over to the real domain **`api.antkart.in`** (GoDaddy A record → the ingress public IP) with a trusted Let's Encrypt **production** certificate (secret `ak-gateway-tls`, dnsNames `["api.antkart.in"]`); host and issuer are driven from Git and reconciled by Argo CD (a `helm upgrade` would be reverted by self-heal). The AKS subnet's custom NSG opens inbound 80/443 from the Internet tag (the bring-your-own-VNet requirement AKS does not handle automatically) — [AKS Guide](guides/aks-guide.md#ingress-and-tls)

**GitOps delivery (Argo CD)**
- The cluster is Git-driven by Argo CD: a dedicated `antkart` AppProject scopes the allowed repository, destination namespace, and resource scope; six Applications (with an ApplicationSet alternative) reconcile the **same** generic Helm chart per service. Adopted with manual-first sync (the only diff was Argo CD's tracking-id annotations, no workload change), and a `replicaCount` change proven to deploy via `git push` alone — [GitOps Guide](guides/gitops-guide.md) · [deploy/argocd/README](../deploy/argocd/README.md)

**CI/CD with GitHub Actions (all six services)**
- Per-service, path-filtered pipelines in two decoupled workflows (Pattern B), **delivered and proven for all six services** (Products, ShoppingCart, Order, Payments, Discount, Gateway): **CI** on pull request (build, unit + in-memory integration tests with coverage, SonarCloud, Trivy fs + Dockerfile scan; all actions pinned to immutable commit SHAs) is enforced by the `master-protection` ruleset (four required checks); **CD** on merge authenticates to Azure with **OIDC** (the `github-oidc` identity, AcrPush only, no cluster access, no secret), builds a **commit-SHA-tagged immutable image**, pushes it to ACR, and bumps `.image.tag` in Git for Argo CD to auto-sync. A change flows from PR to a running pod on the new image tag, hands-free. Each service is a copy of the Products pair with only per-service specifics changed (image repo, Dockerfile, values file; Discount is gRPC, Gateway has no test project and keeps its ingress untouched). The custom-domain cutover to `api.antkart.in` was itself delivered through this GitOps path — a Git change reconciled by Argo CD, no manual cluster action — [DevOps CI/CD Guide](guides/devops-cicd-guide.md) · [ADR-023](adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [ADR-022](adr/ADR-022-cicd-github-actions-oidc.md)

**End-to-end verified on the cluster**
- Verified through the public HTTPS endpoint: browse products, add to cart, and create an order — driving server-authoritative price revalidation, the orchestrated SAGA, stock reservation, order confirmation, cart clearing, and both notification emails delivered via Event Grid → Functions → ACS — [Cluster end-to-end verification](test/README.md#cluster-end-to-end-verification-public-ingress)
- Re-verified after a stop/redeploy: the six services reinstalled from ACR images; three (Order, Payments, Discount) self-healed from `CrashLoopBackOff` automatically once PostgreSQL was started (Kubernetes reconciliation, no pod intervention); ingress/TLS re-enabled and served the existing production certificate on the persisted public IP; and the full Postman journey — including the Razorpay payment-initiate call returning a `razorpayOrderId` (the outbound egress leg) — confirmed every integration path
- Full orchestrated **saga verified end to end** through the public HTTPS endpoint (`api.antkart.in`) — both the payment **success** branch (the order reaches `Paid`) and the payment **failure** branch (the order reaches `PaymentFailed`), exercising stock reservation, the payment outcome, the order state transitions, and the notification emails. This run surfaced and fixed the order state machine's missing `Confirmed → Paid` / `Confirmed → PaymentFailed` transitions — [KI-009](KNOWN_ISSUES.md) · [Cluster end-to-end verification](test/README.md#cluster-end-to-end-verification-public-ingress)

**Observability (logs & traces)**
- Structured logging, distributed tracing, and working correlation delivered across the fleet: Serilog JSON to the console collected by the OMS agent into **Log Analytics** (`ContainerLog`); **OpenTelemetry** traces exported to **Application Insights** (`AppRequests`/`AppDependencies`); cross-service correlation via `X-Correlation-Id` plus `TraceId`/`SpanId` on every log line — [ADR-025](adr/ADR-025-observability-architecture.md) · [Observability — how it is seen](development/5-observability.md)
- **Metrics are not collected.** A self-hosted Prometheus/Grafana (kube-prometheus-stack) was implemented and then **deliberately removed** — operational complexity disproportionate to a two-node dev cluster, and a choice not to present unearned depth. Logs and traces are unaffected; a managed platform is the planned replacement (see Planned) — [ADR-025](adr/ADR-025-observability-architecture.md)

**Infrastructure as code**
- Terraform modules (resource shape) composed by Terragrunt live units per environment over a shared remote-state backend, with a reviewed `plan` before every `apply` — [infrastructure/README](../infrastructure/README.md) · [Infrastructure Guide](guides/infrastructure-guide.md) · [ADR-012](adr/ADR-012-iac-with-terraform-terragrunt.md) · [ADR-013](adr/ADR-013-key-vault-rbac-and-observability-foundation.md) (Key Vault RBAC + observability foundation)

**Documentation**
- Concept primers, the full Architecture Decision Record set, and step-by-step build guides — [ADR index](adr/README.md) · [Development Guide](../DevelopmentGuide.md)

## In progress

_Nothing is in active development right now — the near-term plan below is the immediate queue._

## Planned — near term

_Target: on or before 6 August 2026._

- **Managed observability (metrics) — Datadog evaluation.** Evaluate a managed platform (Datadog under consideration) to provide metrics/dashboards/alerting in place of the removed self-hosted Prometheus/Grafana; logs and traces are already delivered and would consolidate under it if adopted — [Observability — how it is seen](development/5-observability.md) · [ADR-025](adr/ADR-025-observability-architecture.md).
- **Deep-understanding consolidation** — of the delivered platform: Kubernetes, Helm, GitOps / Argo CD, CI/CD, OIDC federated credentials, workload identity, and action SHA pinning.
- **Kubernetes depth at interview level** — probes, resources, configuration, storage, networking, policies, and failure diagnosis applied to the running fleet.
- **API Management spike** — a time-boxed exploration: provision → wire one scenario (JWT validation at the edge) → test → delete. No standing APIM resource — [ADR-020](adr/ADR-020-api-management-managed-edge-gateway.md).
- **Infrastructure provisioning and teardown pipelines** — automated apply and destroy of the environment as code, validated by a full teardown-and-rebuild — [infrastructure/README](../infrastructure/README.md).
- **Architecture diagram set, redrawn** — for the cloud-native platform, with a locked visual language and a C4/Mermaid tooling split; Azure API Management appears in the container view, the network/traffic-path diagram, and the APIM-edge diagram. The existing C4 model and renders describe the earlier Phase 1 microservices platform and are **superseded** — [C4 renders](C4Renders/).
- **The Architect's Playbook** — a concise pre-interview quick-reference covering ~36 concepts, each linking to the ADR or concept guide that holds the detail.

## Planned — future

_After 6 August 2026._

- **Heroic rebuild runbook** — an empty-subscription-to-running-platform runbook, written up and validated by following it end to end.
- **Test guide set** — Full-Cloud E2E, Security / ethical-hacking, and Load / Performance guides. Existing test documentation that describes local/localhost testing is **superseded** — only tests executed against cloud resources are valid going forward — [Testing index](test/README.md).
- **README front door and documentation navigation index** — the public entry point and cross-link map, after the redrawn diagrams land.
- **Job-hunt portfolio assets** — a public, history-free repository, its README, a CV, and LinkedIn.
- **Open technical debt** — KI-002 Discount gRPC token validation; no stock-release compensation on payment failure; and Razorpay signature verification depending on SDK static state (breaks if Payments scales beyond one replica) — [Known Issues Register](KNOWN_ISSUES.md).
- **Roadmap-level documentation** — FinOps / cost management, sovereign and regulated-cloud considerations, and performance and scalability validation.
- **AZ-305 certification** — Azure Solutions Architect Expert.
- **Hardened container base image** — a chiseled/distroless .NET base image published to ACR and consumed by all service images, reducing the attack surface and centralising runtime patching in one place — [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md) (recorded as future work).
- **Security programme** — a cross-cutting programme across identity, network, runtime, supply chain, data, detection, and governance: DAST, Kubernetes network policies, pod-security admission, dependency and image scanning, cloud workload protection, policy enforcement, and audit logging with alerting; with image signing, customer-managed keys, secret rotation, and threat modelling documented. Open security defects awaiting this work are tracked in the [Known Issues Register](KNOWN_ISSUES.md).
- **ISO/IEC 27001 alignment** — implement the applicable controls and document the mapping from control to implementation.
- **Multi-cloud delivery** — deploying the same application codebase to AWS through a separate infrastructure and delivery pipeline, demonstrating portability of the application layer.
- **Service mesh with mutual TLS** — a mesh providing mTLS between services for authenticated, encrypted in-cluster traffic.
- **Shared building blocks as a package feed** — publishing the cross-cutting library as a versioned package for consumption across services.

## Conventions and key facts

| Item | Value |
|------|-------|
| Azure resource naming | `antkart-` prefix (resource groups, registry, Key Vault, Service Bus, and so on) |
| In-cluster naming | `ak-` prefix for Kubernetes Services and ServiceAccounts (`ak-products`, `ak-cart`, `ak-order`, `ak-payments`, `ak-discount`, `ak-gateway`) |
| Kubernetes namespace | `antkart` |
| Container registry | `acrantkartdev.azurecr.io`, images under the `antkart/<service>` namespace |
| Workload-identity federated subject | `system:serviceaccount:antkart:ak-<service>` (exact-match, case-sensitive); audience `api://AzureADTokenExchange` |
| Operational detail | [AKS Guide](guides/aks-guide.md) (cluster, operator access, workload identity, troubleshooting) · [Container Configuration](guides/container-configuration.md) (per-service runtime keys) · [infrastructure/README](../infrastructure/README.md) (module and apply-order map) |

## Maintenance note

This document is updated as items move between **Delivered**, **In progress**, and **Planned**. It stays a summary with links: detailed decisions belong in the [Architecture Decision Records](adr/README.md), and detailed procedures belong in the [guides](guides/). When a claim here and a guide disagree, the guide and the code are authoritative and this document is corrected to match.
