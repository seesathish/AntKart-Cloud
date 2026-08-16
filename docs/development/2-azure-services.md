# Azure services — where it runs

> **Diagrams pending review:** _Azure topology_ and _Network and traffic path_ are carried across as-is and will be reworked.

The platform runs entirely on managed Azure services, provisioned by the [infrastructure-as-code](1-infrastructure-as-code.md) units. The application was migrated off local infrastructure onto these managed services, adopting token-based authentication throughout. Each local Phase-1 component was replaced by a managed equivalent: Keycloak → **Microsoft Entra ID**, RabbitMQ → **Azure Service Bus**, MongoDB → **Cosmos DB (MongoDB API)**, local Postgres → **PostgreSQL Flexible Server**, local Redis → **Azure Managed Redis**, SMTP/Mailhog → **Azure Communication Services**, and ELK → **Azure Monitor (Application Insights / Log Analytics)**.

## Azure topology

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../C4Renders/renders/AzureServices-dark.svg">
  <img alt="AntKart on Azure: a customer request passes through the planned API Management edge into the Kubernetes cluster, where the gateway routes to Products, Cart, Order, Payments and Discount, which use Cosmos DB, Redis and PostgreSQL, publish to Service Bus and Event Grid, and trigger a serverless function that sends email through Communication Services" src="../C4Renders/renders/AzureServices.svg">
</picture>

## Network and traffic path

```mermaid
flowchart TB
    User["Client / Browser"]:::external
    DNS["GoDaddy DNS<br/>api.antkart.in A record"]:::external
    IP["Ingress LoadBalancer<br/>public IP 20.246.197.150"]:::edge
    NGINX["ingress-nginx controller"]:::edge
    TLS["TLS termination<br/>cert-manager · secret ak-gateway-tls<br/>Let's Encrypt prod"]:::edge
    GW["AK.Gateway · Ocelot"]:::service

    subgraph ROUTES["Ocelot /gateway routes"]
        P["products to ak-products"]:::service
        C["cart to ak-cart"]:::service
        O["orders to ak-order"]:::service
        PAY["payments to ak-payments"]:::service
        H["health/* to each service"]:::service
    end

    APIM["Azure API Management<br/>planned edge"]:::edge
    GAP["Not yet provisioned (ADR-020)"]:::issue

    User --> DNS --> IP --> NGINX --> TLS --> GW --> ROUTES
    User -. "planned" .-> APIM -. "planned" .-> IP
    GAP -.-> APIM

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **DNS is external and manual:** a GoDaddy **A record** maps `api.antkart.in` to the ingress LoadBalancer's public IP `20.246.197.150` — not Azure DNS.
- **TLS terminates at the cluster edge:** `ingress-nginx` + `cert-manager` (secret `ak-gateway-tls`, Let's Encrypt **production**), not at the service.
- **One public entry point:** everything enters through `AK.Gateway`; its Ocelot routes fan out to the internal services (`/gateway/products|cart|orders|payments` and `/gateway/health/*`).
- **API Management is planned, not live:** the dashed path and the red-dashed gap annotation mark the managed edge as **not yet provisioned** — today traffic goes DNS → IP directly.

## How it was built

- The migration from local to managed services: [Cloud Migration Guide](../guides/cloud-migration-guide.md).
- Concepts for the resources: [Cosmos DB](../guides/cosmosdb-concepts.md) · [Messaging](../guides/messaging-concepts.md) · [Serverless & Eventing](../guides/serverless-eventing-concepts.md).

## Decisions

- [ADR-014 — Cosmos DB and Azure Service Bus](../adr/ADR-014-cosmosdb-and-servicebus.md)
- [ADR-015 — Messaging Migration to Azure Service Bus](../adr/ADR-015-messaging-migration-to-service-bus.md)
- [ADR-016 — Cosmos DB Data Migration and Workload Identity Foundation](../adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md)
- [ADR-017 — Entra ID, Azure Functions, and Event Grid](../adr/ADR-017-entra-id-functions-eventgrid.md)
- [ADR-019 — Serverless Notification with Azure Functions and Event Grid](../adr/ADR-019-serverless-notification-functions-eventgrid.md)
- [ADR-020 — API Management as the Managed Edge Gateway](../adr/ADR-020-api-management-managed-edge-gateway.md) _(planned)_
- [ADR-021 — Retire the Dedicated Identity Service for Microsoft Entra ID](../adr/ADR-021-retire-identity-service-for-entra.md)

## Open items

- **Azure API Management** (the managed edge) is planned, not provisioned — see [ADR-020](../adr/ADR-020-api-management-managed-edge-gateway.md) and the [Roadmap](../ROADMAP.md).
