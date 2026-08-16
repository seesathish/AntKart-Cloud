# ADR-016 — Products Persistence: Cosmos DB Migration and Workload Identity Foundation

**Status:** Accepted  
**Date:** 2026-05-31  
**Area:** Second Application Code Change  
**Relates to:** ADR-014 (Cosmos DB and Service Bus infrastructure provisioning), ADR-015 (Service Bus token auth)

---

## Context

Phase 1 of AntKart used a local MongoDB Docker container (`mongodb:latest`) as the persistence layer for AK.Products. Six months of product data (300 seed records) lived in a container volume on the developer's machine.

For Phase 2 (Azure deployment), the managed Cosmos DB account (`cosmos-antkart-dev`, MongoDB API, Serverless) was provisioned as part of the core infrastructure. The Cosmos database `antkart-products` exists but is empty — the application still points at localhost.

This ADR covers decisions about:
1. How to connect AK.Products to Cosmos DB without embedding the account key in source control
2. Why the Cosmos auth pattern differs from Service Bus
3. How MongoClient should be registered to satisfy Cosmos's connection model
4. What the partition/shard strategy should be
5. How to lay the groundwork for Workload Identity in AKS now, while AKS doesn't yet exist

---

## Decision 1 — Retrieve the Cosmos connection string from Key Vault at startup, not from appsettings

### Decision

The Cosmos DB connection string is stored in Key Vault as secret `cosmos-connection-string` (written by the Cosmos DB Terraform module). At application startup, `ServiceCollectionExtensions.AddInfrastructure()` calls `SecretClient.GetSecret()` using `DefaultAzureCredential` to retrieve the connection string. The retrieved string is used to construct `MongoClient`. It never appears in `appsettings.json` or any committed file.

`appsettings.json` contains only non-secret references:
```json
"CosmosDb": {
  "KeyVaultUri": "https://kv-antkart-dev.vault.azure.net/",
  "SecretName":  "cosmos-connection-string"
}
```

### Why the Cosmos pattern differs from Service Bus

When messaging was migrated, Service Bus was configured with `h.TokenCredential = new DefaultAzureCredential()`. No connection string. No key. `DefaultAzureCredential` is passed directly to the Service Bus SDK for all operations — authentication is token-based end to end.

Cosmos DB MongoDB API cannot do this at the wire protocol level:

| Layer | Service Bus | Cosmos MongoDB API |
|-------|------------|-------------------|
| Wire protocol | AMQP 1.0 | MongoDB wire protocol (SCRAM-SHA-256 auth) |
| Azure AD token auth | Native — AMQP supports OAuth 2.0 token auth | Not supported in MongoDB wire protocol |
| Key in connection string | No — only namespace FQDN needed | Yes — account key embedded in connection string |
| Can pass DefaultAzureCredential to SDK? | Yes — directly | Only for Key Vault fetch; not for MongoDB wire auth |

Azure AD token auth for Cosmos exists but only through the **Core SQL API** (`CosmosClient`) and specific SDK clients — not through the MongoDB wire protocol. `MongoDB.Driver` communicates exclusively via the MongoDB handshake. It has no mechanism to exchange an Azure AD token for a SCRAM credential.

The pragmatic, secure alternative: store the account key in Key Vault, retrieve it at startup via token auth (`DefaultAzureCredential → AzureCliCredential` locally, `DefaultAzureCredential → WorkloadIdentityCredential` in AKS). Token auth is used for the Key Vault fetch step. The connection string is consumed only in memory.

### Alternatives considered

**Option A (rejected): Connection string in appsettings.json**

```json
"MongoDbSettings": {
  "ConnectionString": "mongodb://cosmos-antkart-dev:<key>@cosmos-antkart-dev.mongo.cosmos.azure.com:10255/?ssl=true&..."
}
```

Rejected because the Cosmos account key is a long-lived credential. Committing it to source control — even in a private repository — violates the principle of least exposure. If the repo is ever made public, or a developer's machine is compromised, the key is leaked. The key must be rotated (disrupting all services) and the git history must be scrubbed.

**Option B (rejected): Environment variable**

Set `ConnectionString` via an OS-level environment variable, never in a file. Rejected because:
- Environment variables are visible to all processes on the machine (not just the application)
- `printenv` or process inspection tools expose them trivially
- A developer working across multiple projects might accidentally set the wrong subscription's key
- The key still exists in plaintext on the machine

**Option C (chosen): Key Vault at startup**

The connection string lives only in Key Vault. The application has no static secret at all. The identity (az login locally; Workload Identity in AKS) is what grants access. Identity can be revoked instantly without touching any config file.

### Consequences

- Developer must run `az login` before starting AK.Products (same requirement as Service Bus — already established during the messaging migration)
- Developer identity needs `Key Vault Secrets User` on `kv-antkart-dev` (documented in DevelopmentGuide Section 5)
- If Key Vault is unreachable, the service fails to start — fail-fast is the intended behaviour
- In AKS: `mi-ak-products-dev` managed identity has `Key Vault Secrets User` — no developer action needed

---

## Decision 2 — Register MongoClient as a singleton in DI

### Decision

`MongoClient` is extracted from `MongoDbContext` and registered explicitly as a singleton in `AddInfrastructure()`. DI injects it into `MongoDbContext`'s production constructor.

```csharp
services.AddSingleton(new MongoClient(mongoClientSettings)); // explicit singleton
services.AddSingleton<MongoDbContext>();                     // receives MongoClient via DI
```

### Rationale

Cosmos DB enforces a per-account connection limit. `MongoClient` manages a connection pool internally — one pool per instance. Creating multiple `MongoClient` instances (one per request, or one per scope) rapidly exhausts Cosmos's connection quota, resulting in 429 (TooManyRequests) errors.

By registering `MongoClient` as a singleton at the DI root level, the container owns the connection pool and guarantees exactly one pool for the lifetime of the process. Even if `MongoDbContext` were accidentally changed to `AddScoped` in the future, the injected `MongoClient` would still be the singleton instance.

The previous pattern (creating `MongoClient` inside `MongoDbContext`'s constructor) was acceptable for native MongoDB but creates an invisible risk with Cosmos. Extracting it to DI makes the singleton intent explicit and auditable.

---

## Decision 3 — Keep the test constructor MongoDbContext(IMongoDatabase) unchanged

### Decision

`MongoDbContext` retains both constructors:
1. `MongoDbContext(MongoClient, IOptions<MongoDbSettings>)` — production
2. `MongoDbContext(IMongoDatabase)` — test injection

The test constructor is used by all 23 infrastructure test methods to inject `Mock<IMongoDatabase>` without touching Key Vault, `MongoClient`, or any real network.

### Rationale

Unit tests must not require Azure access. `ProductRepositoryTests` and `UnitOfWorkTests` mock `IMongoDatabase` and `IMongoCollection<Product>` — they verify repository logic in complete isolation. If the test constructor were removed, tests would need to be rewritten to mock Key Vault or build a real `MongoClientSettings` from a test connection string — creating a network dependency that breaks CI without Azure credentials.

The two-constructor pattern is the correct pattern for infrastructure classes that need to be testable without real infrastructure.

---

## Decision 4 — Partition/shard key strategy for the products collection

### Decision

No shard key is declared explicitly. In serverless mode, Cosmos DB manages partitioning internally using `_id` as the routing key. The partition strategy is documented for future reference only — no schema change is needed.

### Rationale

**Serverless mode**: Cosmos DB Serverless uses a single-region, serverless compute tier. Partitioning is managed automatically. There is no concept of provisioned RU/s per partition. Cross-partition queries consume more RUs but are not blocked or restricted.

**If migrating to provisioned throughput (future)**:

The recommended shard key for `products` would be `CategoryName` ("Men", "Women", "Kids") because:
- It has high cardinality relative to document count (~100 documents per category value)
- Most read queries filter by `CategoryName` — shard key match means single-partition query (cheap)
- Write distribution is even — products are inserted uniformly across categories
- It's already indexed (`idx_category`) — the partition key index is essentially free

The shard key would be set in the Cosmos collection creation step (Terraform) and matched in `MongoClientSettings`. This is a non-breaking schema change in serverless to provisioned migration.

---

## Decision 5 — Workload Identity foundation: create the User-Assigned Managed Identity now, federate when AKS exists

> **Amendment note (2026-07-23).** The workload-identity foundation described in this decision was **superseded in implementation** by [ADR-018](ADR-018-aks-workload-identity-base-image.md), now the authoritative record. In the delivered platform: the module is **`infrastructure/modules/workload-identity/`** (not `modules/identity/`); there is **one identity per service, `id-ak-<service>-dev`, for all six services** (not a single Products-only `mi-ak-products-dev`); and Service Bus access is granted as **`Azure Service Bus Data Sender` + `Azure Service Bus Data Receiver`** (`Azure Service Bus Data Owner` was never implemented). The federated-credential subject is `system:serviceaccount:antkart:ak-<service>`. The original decision narrative below is preserved as the point-in-time record.

### Decision

A Terraform identity module (`infrastructure/modules/identity/`) is created now. It provisions:
- `mi-ak-products-dev` — User-Assigned Managed Identity
- `Key Vault Secrets User` role assignment on `kv-antkart-dev`
- `Azure Service Bus Data Owner` role assignment on `sb-antkart-dev`

The federated credential (linking this identity to AKS pods via OIDC) is **not** created now — the AKS cluster does not yet exist. The federation step is an additive resource added when AKS is introduced.

### Why User-Assigned (not System-Assigned)

AKS Workload Identity federation requires:
1. A `client_id` that can be annotated on a Kubernetes ServiceAccount before the pod starts
2. A resource that exists independently of any VM or AKS node

System-assigned identities are tied to a specific resource (VM, App Service, AKS node pool). Their lifecycle ends with the resource's lifecycle. You cannot pre-create them, and their `client_id` is not stable across resource recreations.

User-assigned identities are independent Azure resources. They persist across resource changes. Their `client_id` is stable. **They are the only type that supports Kubernetes Workload Identity.**

### Why create the identity before AKS exists

Two reasons:
1. **RBAC verification**: Creating the identity and granting roles now lets us verify that the role assignments are correct before AKS is deployed. If an RBAC grant fails (wrong scope, wrong principal type, permission error), we discover it now — not on first pod startup in AKS when multiple changes are happening at once.
2. **Shorter critical path when AKS is introduced**: At that point, the only identity step remaining is adding the federated credential. The harder parts (identity creation, role grants) are already done and validated.

### Federation step when AKS is introduced (informational)

When the AKS cluster is provisioned, the following Terraform resource completes the Workload Identity setup:

```hcl
resource "azurerm_federated_identity_credential" "products" {
  name                = "ak-products-federation"
  resource_group_name = "<resource_group>"
  parent_id           = "<products_identity_id from this module's output>"
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "<aks_oidc_issuer_url>"
  subject             = "system:serviceaccount:ak-products:ak-products-sa"
}
```

The Kubernetes ServiceAccount in the `ak-products` namespace is annotated:
```yaml
metadata:
  annotations:
    azure.workload.identity/client-id: "<products_client_id from this module's output>"
```

After these two steps, a pod in the `ak-products` namespace running as `ak-products-sa` automatically receives a projected service account token. `DefaultAzureCredential` reads it and obtains an Azure token. The pod accesses Key Vault and Service Bus with no static secrets — the same code that runs locally with `az login`.

### Consequences

- `mi-ak-products-dev` incurs no cost (managed identities are free; role assignments are free)
- RBAC changes (adding or removing roles) do not require redeploying the application
- The `products_client_id` and `products_identity_id` outputs must be noted before the federation step
- Future services (Order, Payments, etc.) get their own managed identities when they are migrated to cloud resources

---

## Summary of files changed

### Application code

| File | Change |
|------|--------|
| `AK.Products/AK.Products.Infrastructure/AK.Products.Infrastructure.csproj` | Added `Azure.Identity 1.13.2`, `Azure.Security.KeyVault.Secrets 4.7.0` |
| `AK.Products/AK.Products.Infrastructure/Persistence/CosmosDbSettings.cs` | **New file** — `KeyVaultUri` + `SecretName` settings |
| `AK.Products/AK.Products.Infrastructure/Persistence/MongoDbSettings.cs` | Removed `ConnectionString`; updated `DatabaseName` default to `"antkart-products"` |
| `AK.Products/AK.Products.Infrastructure/Persistence/MongoDbContext.cs` | Production constructor now accepts `MongoClient` singleton; test constructor unchanged |
| `AK.Products/AK.Products.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Key Vault secret fetch + `MongoClient` singleton registration |
| `AK.Products/AK.Products.API/appsettings.json` | Removed `ConnectionString`; updated `DatabaseName`; added `CosmosDb` section |
| `AK.Products/AK.Products.Tests/Infrastructure/MongoDbSettingsTests.cs` | Updated default assertions; added 3 `CosmosDbSettings` tests |

### Terraform infrastructure

| File | Change |
|------|--------|
| `infrastructure/modules/identity/main.tf` | **New** — UAMI + 2 role assignments |
| `infrastructure/modules/identity/variables.tf` | **New** |
| `infrastructure/modules/identity/outputs.tf` | **New** — `products_client_id`, `products_principal_id`, `products_identity_id` |
| `infrastructure/modules/identity/README.md` | **New** |
| `infrastructure/environments/dev/identity/terragrunt.hcl` | **New** — wires identity module to dev, depends on resource-group, key-vault, servicebus |

> **Amendment note (2026-07-23):** these `infrastructure/modules/identity/` and `infrastructure/environments/dev/identity/` paths reflect the original plan. The delivered module is **`infrastructure/modules/workload-identity/`** with a per-service identity for all six services — see the amendment note under Decision 5 and [ADR-018](ADR-018-aks-workload-identity-base-image.md).

**Zero changes to:** ProductRepository, ProductClassMap, ProductSeeder, UnitOfWork, all domain entities, all other services, all integration events, consumers, SAGA, outbox. Test count: 618 → 620 (2 net added).
