# Container Configuration Reference

This document lists the runtime configuration each containerized AntKart service requires. It is
the source of truth for the environment variables and secrets that must be supplied when the
services run in Kubernetes (Helm values, ConfigMaps, and Key Vault–backed secrets).

## Principles

- **No secrets in images.** Container images contain only non-secret defaults. All secret values
  (database connection strings, external API keys) are resolved at runtime from Azure Key Vault via
  the pod's managed identity (`DefaultAzureCredential`), or injected as environment variables.
- **Key Vault is a configuration source.** When `KeyVault:Uri` is set, each service folds every
  vault secret into `IConfiguration` at startup. Key Vault secret names use `--` as the section
  separator, which maps to `:` in configuration (e.g. secret `ConnectionStrings--Postgres` →
  config key `ConnectionStrings:Postgres`).
- **Environment variables override committed defaults.** .NET binds environment variables using `__`
  as the section separator (e.g. `ProductsApi__BaseUrl` → `ProductsApi:BaseUrl`). Every value in a
  committed `appsettings.json` is a local-development default intended to be overridden in the cluster.
- **Ports.** Every service binds Kestrel to `8080` inside the container (the .NET 9 base image
  default, `ASPNETCORE_HTTP_PORTS=8080`). Non-root containers (`USER $APP_UID`) cannot bind ports
  below 1024. Kubernetes Services should target port `8080`.

## Configuration source legend

| Source | Meaning |
|--------|---------|
| **Key Vault** | Secret. Supplied by the vault at runtime via managed identity. Never committed. |
| **Env / ConfigMap** | Non-secret. Supplied as an environment variable (typically from a ConfigMap). |
| **Built-in default** | Ships in the image (base-image environment); no action required. |

---

## AK.Products.API

REST catalogue service. Data store: Azure Cosmos DB (MongoDB API).

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| `ProductsCosmosConnectionString` | Cosmos DB (MongoDB API) connection string | **Key Vault** (secret) | `mongodb://localhost:27017` fallback |
| `MongoDbSettings:DatabaseName` | Cosmos database name | Env / ConfigMap | `antkart-products` |
| `MongoDbSettings:ProductsCollection` | Cosmos collection name | Env / ConfigMap | `products` |
| `KeyVault:Uri` | Key Vault endpoint for secret loading | Env / ConfigMap | `https://kv-antkart-dev.vault.azure.net/` |
| `DiscountGrpc:Address` | Address of the AK.Discount gRPC service (optional dependency) | Env / ConfigMap | `http://localhost:5001` |
| `ServiceBus:FullyQualifiedNamespace` | Azure Service Bus namespace (Entra auth, no connection string) | Env / ConfigMap | `sb-antkart-dev.servicebus.windows.net` |
| `Entra:TenantId` / `Entra:Audience` / `Entra:ClientId` / `Entra:Instance` | JWT validation against Microsoft Entra ID | Env / ConfigMap | committed (non-secret) |
| `Seeding:RunOnStartup` | Opt-in boot-time seeding flag | Env / ConfigMap | `false` |

**Flag:** In Kubernetes, `DiscountGrpc:Address` must point at the in-cluster Discount Service DNS
name (e.g. `http://ak-discount-grpc:8080`), not `localhost:5001`.

---

## AK.Discount.Grpc

gRPC discount service. Data store: PostgreSQL (`AKDiscountDb`). Serves HTTP/2 (h2c) on 8080.

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| `ConnectionStrings:DiscountDb` | PostgreSQL connection string | **Key Vault** (`ConnectionStrings--DiscountDb`) | `Host=localhost;...;Password=postgres` (committed dev default) |
| `KeyVault:Uri` | Key Vault endpoint | Env / ConfigMap | `https://kv-antkart-dev.vault.azure.net/` |

**Flag:** The committed `appsettings.json` contains a localhost PostgreSQL connection string with a
plaintext password. This is a **development-only default** and must be overridden by the vaulted
`ConnectionStrings--DiscountDb` secret in the cluster. Consumers of this service (AK.Products) must
use HTTP/2 — the endpoint negotiates h2c.

---

## AK.ShoppingCart.API

REST cart service. Data store: Redis.

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| `RedisSettings:ConnectionString` | Redis connection string | **Key Vault** or Env (secret if it carries a password) | `localhost:6379` |
| `RedisSettings:InstanceName` | Redis key prefix | Env / ConfigMap | `AKCart:` |
| `RedisSettings:CartExpiryDays` | Cart TTL in days | Env / ConfigMap | `30` |
| `KeyVault:Uri` | Key Vault endpoint | Env / ConfigMap | `https://kv-antkart-dev.vault.azure.net/` |
| `ServiceBus:FullyQualifiedNamespace` | Azure Service Bus namespace | Env / ConfigMap | `sb-antkart-dev.servicebus.windows.net` |
| `Entra:*` | JWT validation | Env / ConfigMap | committed (non-secret) |

**Flag:** `RedisSettings:ConnectionString` is `localhost:6379` in source. In the cluster it must point
at the managed Redis endpoint; if the endpoint requires an access key, supply the full connection
string from Key Vault (treat as secret).

---

## AK.Order.API

REST order service. Data store: PostgreSQL (`AKOrdersDb`). Calls AK.Products for price revalidation.

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| `ConnectionStrings:Postgres` | PostgreSQL connection string | **Key Vault** (`ConnectionStrings--Postgres`) | `Host=localhost;...;Password=postgres` (committed dev default) |
| `ProductsApi:BaseUrl` | Base URL of AK.Products for server-side price revalidation | Env / ConfigMap | `http://localhost:5077/` |
| `KeyVault:Uri` | Key Vault endpoint | Env / ConfigMap | `https://kv-antkart-dev.vault.azure.net/` |
| `ServiceBus:FullyQualifiedNamespace` | Azure Service Bus namespace | Env / ConfigMap | `sb-antkart-dev.servicebus.windows.net` |
| `EventGrid:TopicEndpoint` | Event Grid topic for notification side-effects | Env / ConfigMap | `https://evgt-antkart-dev...` |
| `Entra:*` | JWT validation | Env / ConfigMap | committed (non-secret) |

**Flag:** `ProductsApi:BaseUrl` (`http://localhost:5077/`) must be overridden to the in-cluster
Products Service DNS name (e.g. `http://ak-products:8080/`). The committed PostgreSQL default has
a plaintext password and must be replaced by the vaulted `ConnectionStrings--Postgres` secret.

---

## AK.Payments.API

REST payment service. Data store: PostgreSQL (`AKPaymentsDb`). External: Razorpay.

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| `ConnectionStrings:PaymentsDb` | PostgreSQL connection string | **Key Vault** (`ConnectionStrings--PaymentsDb`) | none — service throws if missing |
| `Razorpay:KeyId` | Razorpay sandbox key id | **Key Vault** (`Razorpay--KeyId`) | empty placeholder |
| `Razorpay:KeySecret` | Razorpay sandbox key secret | **Key Vault** (`Razorpay--KeySecret`) | empty placeholder |
| `KeyVault:Uri` | Key Vault endpoint | Env / ConfigMap | `https://kv-antkart-dev.vault.azure.net/` |
| `ServiceBus:FullyQualifiedNamespace` | Azure Service Bus namespace | Env / ConfigMap | `sb-antkart-dev.servicebus.windows.net` |
| `EventGrid:TopicEndpoint` | Event Grid topic for notification side-effects | Env / ConfigMap | `https://evgt-antkart-dev...` |
| `Entra:*` | JWT validation | Env / ConfigMap | committed (non-secret) |

**Flag:** `ConnectionStrings:PaymentsDb` has **no committed default** — the service fails to start if
it is not supplied, so the vaulted `ConnectionStrings--PaymentsDb` secret (or an env override) is
mandatory. Razorpay credentials are empty placeholders in source and must come from Key Vault.

---

## AK.Gateway.API

Ocelot API gateway. Single entry point; routes to the four REST services. No database.

| Config key | Purpose | Source in Kubernetes | Local default |
|------------|---------|----------------------|---------------|
| Ocelot `Routes[].DownstreamHostAndPorts` | Downstream service host/port for each route | Env / ConfigMap (mount `ocelot.json`) | `ak-*-api` hosts, port `8080` |
| `GlobalConfiguration:BaseUrl` | External base URL used when building redirect/Location headers | Env / ConfigMap | `http://ak-gateway:8080` (in-cluster); `http://localhost:8000` for a local run |
| `Entra:*` | Edge JWT validation | Env / ConfigMap | committed (non-secret) |

**Flag:** `ocelot.json` hard-codes downstream hosts (`ak-products`, `ak-cart`,
`ak-order`, `ak-payments`) on port `8080`. These must match the in-cluster Kubernetes Service
names; supply the routing file via a mounted ConfigMap so it can be adjusted per environment without
rebuilding the image. `GlobalConfiguration:BaseUrl` should be set to the gateway's external URL.

---

## Cross-cutting: identity & Azure access

All services authenticate to Azure resources (Key Vault, Service Bus, Event Grid, Cosmos DB via
managed identity where applicable, ACS) using `DefaultAzureCredential`. In Kubernetes this resolves
to a **workload identity** bound to the pod's service account. No Azure credentials are supplied as
environment variables or baked into images. The required role assignments (Key Vault Secrets User,
Service Bus Data Sender/Receiver, Event Grid Data Sender, etc.) are provisioned as infrastructure.

## Summary of values that MUST be externalized

| Value | Services | Mechanism |
|-------|----------|-----------|
| Cosmos DB connection string | Products | Key Vault (`ProductsCosmosConnectionString`) |
| PostgreSQL connection strings | Discount, Order, Payments | Key Vault (`ConnectionStrings--*`) |
| Redis connection string | ShoppingCart | Key Vault / Env (secret if keyed) |
| Razorpay credentials | Payments | Key Vault (`Razorpay--KeyId`, `Razorpay--KeySecret`) |
| In-cluster service URLs (`DiscountGrpc:Address`, `ProductsApi:BaseUrl`, Ocelot downstream hosts) | Products, Order, Gateway | Env / ConfigMap |
| Service Bus / Event Grid / Key Vault endpoints | multiple | Env / ConfigMap (non-secret) |
| Entra ID settings | all | Env / ConfigMap (non-secret) |
