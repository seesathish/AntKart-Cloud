# Cloud Migration Guide

**Purpose:** How the application is migrated from local infrastructure to managed cloud services — managed data stores, messaging, identity, and secret storage — and the patterns that make the services genuinely cloud-native.

The guide is built one migration step at a time. Each step follows the same rhythm — **Understand → Build → Execute → Verify** — and subsequent steps are added as they are delivered. For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md).

---

## Step 1 — Secret-less Configuration from Azure Key Vault

This is the foundation every later migration step builds on. Before a service can be pointed at a managed database, broker, or identity provider, it needs a way to read the connection details for those services **without storing any secret in source control**. The pattern is: secrets live in Azure Key Vault, and each service reads them at startup using its own Microsoft Entra identity — the developer's Azure CLI sign-in locally, a managed identity in the cloud.

### Understand

**`DefaultAzureCredential` — one credential, resolved by environment.** `DefaultAzureCredential` (from the `Azure.Identity` SDK) is a **credential chain**: an ordered list of credential sources it tries in turn, using the first that succeeds.

- **Locally**, the chain resolves to the **developer's Azure CLI sign-in** (`az login`). The code authenticates as *you*.
- **In the cloud**, it resolves to the **managed identity** of the resource the code runs on (an AKS workload identity, or a Function / App Service system-assigned identity).

The same code runs in both places — the environment decides which identity is used, and **no secret is ever stored** to make it work.

**The Key Vault configuration provider.** .NET configuration is layered from sources (appsettings, environment variables, …). The Azure Key Vault configuration provider adds the vault as **one more source**: at startup it uses `DefaultAzureCredential` to authenticate to the vault, reads every secret, and folds them into `IConfiguration`. The application then reads a value such as `MongoDbSettings:ConnectionString` by name exactly as before — but the value comes from the vault, and the **secret is never written into appsettings or committed**. (Key Vault secret names use `--` where configuration uses `:`, so a secret named `MongoDbSettings--ConnectionString` becomes the configuration key `MongoDbSettings:ConnectionString`.)

**Two different identities — do not confuse them.** This step is about the **service's own identity authenticating to Key Vault** (so the service can read its configuration). That is entirely separate from **end-user authentication** — the JWT a caller presents to the API — which is handled by the later identity-migration step. Here, the only identity in play is the service's: yours locally, a managed identity in the cloud.

**Local prerequisite — the role.** Authenticating proves *who* the identity is; it grants nothing until a role assignment says *what* it may do. To read secrets locally, the developer's identity must hold the **Key Vault Secrets User** role on the vault (a read-only data-plane role). Granting it is part of the Execute steps below.

For the foundations behind these ideas, see the [Identity concepts primer](identity-concepts.md).

### Build

The reusable piece is a single extension method in `AK.BuildingBlocks`, `AddAzureKeyVaultConfiguration`, plus two package references. Reading it line by line:

```csharp
public static IConfigurationBuilder AddAzureKeyVaultConfiguration(
    this IConfigurationBuilder builder,
    IConfiguration configuration)
{
    var keyVaultUri = configuration["KeyVault:Uri"];
    if (string.IsNullOrWhiteSpace(keyVaultUri))
        return builder; // No vault configured — keep running from local configuration only.

    var credential = new DefaultAzureCredential();

    builder.AddAzureKeyVault(new Uri(keyVaultUri), credential);
    return builder;
}
```

- **It reads `KeyVault:Uri` from the configuration already loaded** (appsettings / environment variables). The vault URI is **not a secret**, so it is safe to commit.
- **The provider is conditional.** If `KeyVault:Uri` is absent, the method returns without doing anything, so the service still runs purely from local configuration — useful for offline development or a test host with no vault. The same build therefore runs **both** with and without a vault; only the presence of the URI switches the vault on.
- **`DefaultAzureCredential` is constructed with no options.** The default chain already covers both target environments — a managed identity (cloud) and the Azure CLI login (local). For a **user-assigned** managed identity you would name it explicitly via `DefaultAzureCredentialOptions.ManagedIdentityClientId`; the current setup (developer CLI locally, a system-assigned identity in the cloud) needs no options.
- **`AddAzureKeyVault(uri, credential)`** registers the vault as a configuration source using that credential.

The two packages, pinned to versions consistent with the repo's .NET 9 stack, are added to `AK.BuildingBlocks`:

```xml
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.3.2" />
```

A service opts in by calling the extension in `Program.cs` **before anything reads configuration**, and the vault URI is set in its `appsettings.json`:

```csharp
// before AddSerilogLogging and any service registration:
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);
```

```json
"KeyVault": {
  "Uri": "https://kv-antkart-dev.vault.azure.net/"
}
```

In this step `AK.Products` is wired as the proof; the same one-line call is how every other service adopts the pattern.

### Execute

These steps grant the developer identity read access, store a test secret, and run the service so it loads that secret from the vault. Replace `<subscription-id>` and `<resource-group>` with the dev values.

**1. Grant your developer identity the Key Vault Secrets User role on the vault.**

```bash
# Your Entra object id (the signed-in developer)
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)

# The vault's resource id (scope for the role assignment)
VAULT_ID=$(az keyvault show --name kv-antkart-dev --query id -o tsv)

az role assignment create \
  --assignee "$OBJECT_ID" \
  --role "Key Vault Secrets User" \
  --scope "$VAULT_ID"
```

**2. Store a test secret in the vault.**

```bash
az keyvault secret set \
  --vault-name kv-antkart-dev \
  --name "Demo--Greeting" \
  --value "hello-from-key-vault"
```

(The `--` in the name maps to the configuration key `Demo:Greeting`.)

**3. Run the service locally** — with an active `az login` session — so it authenticates as your identity and loads the vault:

```bash
cd AK.Products/AK.Products.API && dotnet run
```

### Verify

Confirm the service started and resolved its configuration from Key Vault **without printing any secret value**. On startup the service logs a single non-secret confirmation line:

```
[HH:mm:ss INF] [AK.Products.API] Program: Key Vault configuration loaded from https://kv-antkart-dev.vault.azure.net/
```

- If the line reads `Key Vault configuration source not configured (KeyVault:Uri absent)…`, the URI is not being picked up — check `appsettings.json`.
- If startup fails with an authorization error from Key Vault, the developer identity is missing the **Key Vault Secrets User** role (step 1) or there is no active `az login` session.

The log line reports only the **vault URI** (a non-secret value) and never the secret contents — secrets remain in the vault and in memory only. The service is now reading configuration from Key Vault using its own Entra identity, with no connection strings committed: the foundation the remaining migration steps build on.

### Note on data seeding

Startup auto-seeding is **disabled by default** for cloud-native operation. A service must not crash on boot, nor mutate the data store as a side effect of starting, simply because a store is momentarily unavailable. The `AK.Products` seeder is therefore gated behind a `Seeding:RunOnStartup` flag (default `false`) and the startup call is wrapped so that a seed failure logs a warning and the application still starts. Routine data seeding is instead performed as a **deliberate, separate operation** by a dedicated loader (introduced in the test-enablement step), not on application start.

---

## Step 2a — JWT Validation with Microsoft Entra ID

This step migrates the platform's cross-cutting **token validation** from the previous identity provider to **Microsoft Entra ID**, and standardises authorization on the flat `roles` claim Entra emits. It is the authentication half of the identity migration; reworking the identity *service* (login, registration, admin) follows separately.

### Understand

**Authentication vs authorization.** Two distinct questions. *Authentication* asks "is this a genuine, current token from the right issuer, meant for this API?" *Authorization* asks "what is this caller allowed to do?" They are answered by different mechanisms and must not be conflated.

**The four validation checks (authentication).** A bearer token is accepted only if all four hold:

1. **Issuer** — the token's `iss` is our tenant's **Entra v2 issuer** (`https://login.microsoftonline.com/<tenant-id>/v2.0`).
2. **Audience** — the token's `aud` is **this API's app registration** (its identifier URI `api://antkart-api-dev`), so a token minted for another API cannot be replayed here.
3. **Signature** — the token is signed by one of **Entra's published OIDC signing keys**, which the middleware fetches (and rotates) automatically from the tenant's OIDC metadata — no key is stored.
4. **Lifetime** — the token is **not expired** and not used before its valid-from time.

**The flat roles claim (authorization).** Once authenticated, authorization is driven by **app roles**. Entra issues them in a **flat, top-level `roles` claim** (a JSON array of role names). The previous provider nested roles under a `realm_access.roles` structure that each service had to unpack itself. Consuming the **flat `roles` claim** — by mapping the framework's role-claim type to `roles` — is what makes role checks **identical across every service**: the same `RequireRole("admin")` works in the REST APIs and in the gRPC interceptor. Standardising on this claim is also what **resolves KI-001**, the gRPC interceptor that previously read the nested structure and would have failed admin authorization under Entra tokens.

**This is the service's identity boundary, not the user's login.** Validating an incoming token is separate from *issuing* one. Token issuance and the login/registration/admin flows are the identity-service rework, handled in the next step.

### Build

The change is centred on one shared component, `AK.BuildingBlocks.Authentication`, with teaching comments:

- **`EntraSettings`** — a typed, **non-secret** settings record (`Instance`, `TenantId`, `Audience`) bound from the committed `Entra` configuration section. It derives the authority and the expected issuer as `{Instance}/{TenantId}/v2.0`.
- **`AddEntraAuthentication`** — configures JWT bearer validation:
  - `Authority` points at the Entra v2 OIDC endpoint, so signing keys are downloaded and rotated automatically.
  - `TokenValidationParameters` turns on the four checks above (`ValidateIssuer` + `ValidIssuer`, `ValidateAudience` + `ValidAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey`).
  - `RoleClaimType = "roles"` makes `[Authorize(Roles=…)]`, `RequireRole("admin")`, and the `admin` policy read the **flat** claim directly — no nested parsing.
  - `MapInboundClaims = false` keeps claims under their original JWT names.
- **`UseEntraAuth`** — registers `UseAuthentication()` then `UseAuthorization()` in the correct order.
- **Per service** — every API (`Products`, `ShoppingCart`, `Order`, `Payments`, `Notification`, `Gateway`, `UserIdentity`) calls `AddEntraAuthentication` / `UseEntraAuth` and carries an `Entra` settings section in place of the old provider section. Business logic is unchanged.
- **gRPC interceptor (KI-001)** — `AK.Discount.Grpc`'s `AuthInterceptor` now reads the flat `roles` claim, so admin write RPCs authorize correctly under Entra tokens; read-only RPCs are unchanged.

### Execute

The Entra settings are **non-secret and committed** (`Entra:Instance`, `Entra:TenantId`, `Entra:Audience`); any value not yet finalized is marked "(to be updated)". To obtain a token for local testing, request one for the API's identifier URI:

```bash
az account get-access-token --resource api://antkart-api-dev --query accessToken -o tsv
```

For the token to carry `roles`, the **caller must be assigned the relevant app role** on the API app registration (for example, the `admin` app role for admin-only operations); without an assignment the token authenticates but carries no `roles` claim.

### Verify

- Calling a **protected endpoint** with a valid Entra token (correct issuer, audience, lifetime, signature) **succeeds**.
- An **admin-only** operation succeeds **only** when the token's flat claim carries `roles: ["admin"]`; a token without it is rejected with 403.
- A **missing, expired, or wrong-audience** token is rejected with 401.

You can inspect a token's claims by decoding it (for example at a JWT decoder) and confirming `aud` is `api://antkart-api-dev`, `iss` ends in `/v2.0`, and `roles` contains the expected role — without sharing the token, which is a credential.

---

## Step 2b — Retire the Dedicated Identity Service

With token validation delegated to Entra (Step 2a), the application no longer needs a service of its own to handle identity. This step removes it. The decision is recorded in [ADR-021](../adr/ADR-021-retire-identity-service-for-entra.md).

### Understand

The application baseline shipped a dedicated identity microservice — a thin proxy over a self-hosted identity provider for login, registration, token refresh, `/me`, and basic user/role administration. Under Microsoft Entra ID, every responsibility it held now lives elsewhere:

- **Token issuance** — Entra issues access tokens directly to clients through standard OAuth 2.0 / OpenID Connect flows. The application issues nothing.
- **Token validation** — already cross-cutting after Step 2a: each service validates Entra tokens through the shared building-blocks authentication.
- **User lifecycle and app-role assignment** — operational concerns managed in **Entra (portal or Microsoft Graph)**, exercised during test enablement — not application endpoints.

With issuance handled by Entra, validation handled in every service, and administration handled operationally, a dedicated identity service has no remaining purpose. Removing it is a **deliberate simplification** of the architecture — one fewer service to build, deploy, secure, and operate.

### Build

What is removed, and why:

- **The identity service projects (API and tests)** and their solution entries — the service is obsolete.
- **The gateway route** that forwarded to it — there is nothing downstream to route to.
- **The provider-specific settings type** that only the identity service consumed, and any code left dead solely by its removal.
- **References across the solution** to the identity service and the former provider, so the codebase reflects the Entra-native model. (Rendered C4 architecture diagrams are intentionally **not** regenerated here — they are updated after the migration round and still show the pre-migration topology.)

The notification "welcome" consumer is **retained** as a forward-compatible seam: in the Entra-native model its trigger is sourced externally (an Entra/Graph signal on user provisioning) rather than from an application identity service.

### Execute / Verify

- The solution **builds cleanly** without the identity service (`dotnet build` → 0 warnings).
- The **gateway starts** without its route to the identity service.
- The **remaining test suite passes**; the total decreases by the retired service's tests.
- No references to the former identity provider remain in the codebase.

---

## Step 2c — Interactive Sign-in with OAuth2 Authorization Code + PKCE

Step 2a made every service a token **validator**. This step covers the other side: how an **interactive client** obtains the token in the first place, and the two pieces of Entra app-registration configuration the platform depends on — **app roles** (for authorization) and the **`email` optional claim** (for user-derived notification recipients). The first-principles walkthrough of the flow is in the [OAuth2 Authorization Code + PKCE concepts guide](oauth2-pkce-concepts.md).

### Understand

**Delegated user tokens.** A client acquires a **delegated** access token — one that represents a **signed-in user**, not the application acting alone. The token carries the user's identity (`sub`, `name`, and — once configured — `email`) and the **app roles** assigned to that user. Every user-scoped operation derives the caller from this token, never from a request path or body (the platform's "never trust the client" rule).

**Authorization Code + PKCE, no stored secret.** Interactive clients here are **public clients** (Postman, a single-page or desktop app) that cannot hold a secret. They use the **Authorization Code flow with PKCE** (Proof Key for Code Exchange): the client invents a one-time secret per sign-in and proves possession of it at the token exchange, so a stolen authorization code is useless on its own. No client secret is stored anywhere. See the [concepts guide](oauth2-pkce-concepts.md) for the mechanics.

**App roles drive authorization.** App roles are declared on the API's Entra app registration and assigned to users or groups. Entra emits them in the **flat `roles` claim** that Step 2a authorizes on, so the same `RequireRole("admin")` works across every service. A token for a caller with no role assignment authenticates but carries no `roles` claim.

**The `email` optional claim.** Entra does **not** include the user's email in an access token by default. The platform configures the **`email` optional claim** on the app registration so delegated tokens carry it. This is what lets the notification path address a message to the signed-in user: the recipient is **derived from the token's `email` claim**, not supplied by the client. Without this claim, user-derived notification recipients would be unavailable.

### Build

- **API app registration** exposes the API (application ID URI `api://antkart-api-dev`), declares the **app roles**, and adds **`email`** to the access-token **optional claims**.
- **Public client registration** for the interactive client, with its redirect URI registered as a **public/native** client and the Authorization Code + PKCE grant — **no client secret**.
- **Reading the identity in code** uses the existing building-blocks helpers (`HttpContextExtensions`): `GetUserId()` (the `sub` claim), `GetUserEmail()` (the `email` claim), and `GetUserDisplayName()` — so every service reads the caller's identity the same way.

### Execute

Acquire a delegated token for the API through the interactive client using Authorization Code + PKCE (the concepts guide lists the exact authorize/token fields). Ensure the signed-in user holds the relevant **app-role assignment** on the API app registration for any role-gated operation.

### Verify

- The delegated token's `aud` is `api://antkart-api-dev`, its `iss` ends in `/v2.0`, and it carries `sub`, the assigned `roles`, and — with the optional claim configured — `email`.
- A protected endpoint accepts the token; a **user-scoped** operation resolves the caller from the token (`sub` / `email`), never from the request body.
- An admin-only operation succeeds only when `roles` contains the required role.

---

## Step 3 — Messaging on Azure Service Bus

This step moves the messaging transport from the self-hosted broker to **Azure Service Bus**, authenticated with **Microsoft Entra** (no SAS key or connection string) and using the topology **provisioned by infrastructure-as-code**. The decision is recorded in [ADR-015](../adr/ADR-015-messaging-migration-to-service-bus.md).

### Understand

**MassTransit abstracts the transport.** The consumers, the order saga, and every `Publish`/`Send` call are written against MassTransit's API, not the broker's. Changing the transport therefore touches **only the bus configuration** — the business logic, the saga state machine, and the integration-event contracts are unchanged. (This is exactly what keeps the in-memory `ITestHarness` integration tests transport-agnostic and green.)

**Topology is owned by infrastructure-as-code.** The queues, the topic, and its subscriptions are provisioned by the platform's IaC — the single source of truth — not created by the application at runtime. The application's managed identity is granted only **Azure Service Bus Data Sender / Data Receiver**, never **Manage**, so it *cannot* create or alter entities. MassTransit is configured to **send to and receive from the existing provisioned entities and to not deploy or manage topology**. This keeps a clear separation: the platform owns *what* exists; the application only *uses* it, with least privilege.

**Entra authentication, no secret.** The bus connects to the namespace by its fully-qualified hostname (`sb-antkart-dev.servicebus.windows.net` — non-secret, committed) using `DefaultAzureCredential`: the developer's Azure CLI sign-in locally, the resource's managed identity in the cloud. There is no connection string, consistent with the secret-less posture established in the earlier steps.

### Build

The change is centred on one shared helper, `AddAzureServiceBusMassTransit` in `AK.BuildingBlocks.Messaging`, which mirrors the previous broker helper one-for-one:

- It reads the non-secret `ServiceBus:FullyQualifiedNamespace` setting.
- It keeps the same kebab-case endpoint-name formatter (per-service prefix), so endpoint naming — and the business wiring — is unchanged; only the transport differs.
- It configures `UsingAzureServiceBus` with `host.TokenCredential = new DefaultAzureCredential()` (Entra auth) and a transport-agnostic message-retry policy, then binds the registered consumers/saga to the provisioned entities.

Per service, the Infrastructure `ServiceCollectionExtensions` swaps the single call from the broker helper to `AddAzureServiceBusMassTransit(...)`; the registered consumers and saga are untouched. The packages move from `MassTransit.RabbitMQ` to `MassTransit.Azure.ServiceBus.Core` (the broker transport package is removed entirely), while `MassTransit` and `MassTransit.EntityFrameworkCore` (outbox/saga) stay. Each service's `appsettings` drops its old broker block and gains `ServiceBus:FullyQualifiedNamespace`.

**Removed:** the orphaned welcome-notification flow. The `UserRegisteredIntegrationEvent` contract and the Notification `UserRegisteredConsumer` were published only by the retired identity service and are now dead; both are deleted, and the affected test is adjusted so the remaining live consumers stay covered.

### Execute

Grant your developer identity the Service Bus data roles on the namespace (Send + Receive), set the non-secret namespace, and run a service locally so it connects over Entra:

```bash
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
SB_ID=$(az servicebus namespace show --name sb-antkart-dev --resource-group rg-antkart-dev-eastus --query id -o tsv)

az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "Azure Service Bus Data Sender"   --scope "$SB_ID"
az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "Azure Service Bus Data Receiver" --scope "$SB_ID"
```

The namespace is configured in each service's `appsettings.json`:

```json
"ServiceBus": {
  "FullyQualifiedNamespace": "sb-antkart-dev.servicebus.windows.net"
}
```

Then run a service with an active `az login` session, e.g. `cd AK.Order/AK.Order.API && dotnet run`.

### Verify

- A service **starts and connects to Service Bus over Entra** — and there is **no broker (RabbitMQ) connection warning**, because the transport is now Service Bus.
- The **in-memory integration tests pass** (`dotnet test`) — they use the `ITestHarness` and require no Service Bus.
- Full **end-to-end SAGA over Service Bus** (publish → reserve stock → payment → notification across the provisioned entities) is verified during the **test-enablement step**.

---

## Step 4 — Product Catalogue on Azure Cosmos DB (MongoDB API)

This step moves the **AK.Products** data store from a self-hosted MongoDB to **Azure Cosmos DB using its MongoDB API**, with the connection string held in **Key Vault** (no secret in config) and a single-field, hashed **shard (partition) key** on the product id. The decision is recorded in [ADR-016](../adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md).

### Understand

**The MongoDB API is wire-compatible.** Cosmos DB's MongoDB API speaks the MongoDB wire protocol, so the existing **MongoDB driver and almost all of the data-access code are unchanged**. The migration is primarily a **connection-string change**, plus one Cosmos-specific design decision: the **shard (partition) key**.

**Partitioning, in plain terms.** Cosmos physically distributes a collection's documents across partitions by the shard key. Choosing it well is what keeps the database fast and cheap:

- A **high-cardinality** key spreads documents evenly and avoids a **hot partition** (one partition taking most of the traffic while others sit idle). The **product id** has one distinct value per product — ideal.
- **Point-reads by id** land on a **single partition** and are cheap (≈1 RU).
- **Category / sub-category browse** is a **low-cost cross-partition query** at this catalogue scale, served by **secondary indexes** on those fields.

The key is therefore the **product id, hashed**. Per the current Cosmos DB for MongoDB documentation, the **RU-based (serverless) API requires a single-field, hashed shard key** (range and compound shard keys are a vCore-tier feature), which matches this choice. In a document the product id is stored as `_id`, so the shard key is `{ "_id": "hashed" }`.

**The connection string is a secret.** It is **retrieved from Key Vault** via the configuration foundation built in Step 1, so it **never appears in committed config**. `appsettings` carries only non-secret values — the database and collection names.

### Build

- **Configuration / secret.** The Cosmos connection string is stored as the Key Vault secret **`ProductsCosmosConnectionString`**. The Step 1 Key Vault configuration provider loads it into `IConfiguration`; the Products Infrastructure registration reads `configuration["ProductsCosmosConnectionString"]` and sets it on `MongoDbSettings.ConnectionString` (via `PostConfigure`). `appsettings` keeps only `DatabaseName` (`antkart-products`, the provisioned Cosmos database) and `ProductsCollection` (`products`) — **no connection string**. When the secret is absent (offline development), the non-secret local default (`mongodb://localhost:27017`) applies.
- **Shard key.** On first construction, `MongoDbContext` ensures the collection is sharded on `{ "_id": "hashed" }` via the `shardCollection` command. The command is a no-op if the collection is already sharded, and is safely skipped on a standalone MongoDB (which does not support sharding) so local development still runs.
- **Indexes.** The category, sub-category, and status secondary indexes are kept (supported single-field indexes). Two Mongo-only constructs are adjusted for Cosmos: the SKU index is made **non-unique** (a unique index on a sharded collection must include the shard key, so SKU uniqueness moves to the data/seed layer), and the **text index is removed** (Cosmos DB for MongoDB API does not support `$text` indexes). The MongoDB driver, the repository, the class map, and all query code are otherwise unchanged.

### Execute

Store the connection string in Key Vault (so it never touches the repo), then run Products so it resolves the secret from the vault:

```bash
# Get the Cosmos account's primary MongoDB connection string and store it as a Key Vault secret.
CONN=$(az cosmosdb keys list --name cosmos-antkart-dev --resource-group rg-antkart-dev-eastus \
  --type connection-strings --query "connectionStrings[0].connectionString" -o tsv)

az keyvault secret set --vault-name kv-antkart-dev --name "ProductsCosmosConnectionString" --value "$CONN"
```

The app reads it from the configuration key **`ProductsCosmosConnectionString`** (loaded from Key Vault). Run with an active `az login` session that can read the vault:

```bash
cd AK.Products/AK.Products.API && dotnet run
```

### Verify

- Products **starts and connects to Cosmos DB** using the vaulted connection string — there is **no localhost MongoDB** involved.
- A **create** and a **read** of a single product succeed against Cosmos (a point-read by id is single-partition).
- Startup seeding remains **opt-in** (`Seeding:RunOnStartup` default `false`); the **bulk seed of the full catalogue** is the later **test-enablement step**, not this one.

---

## Step 5 — Serverless Side-Effects with Event Grid and Azure Functions

This step brings the provisioned **Azure Event Grid** custom topic (`evgt-antkart-dev`) and **Azure Function App** (`func-antkart-notifications-dev`) into use for lightweight, **fire-and-forget side-effects** — starting with notification. It is a deliberate counterpart to Step 3: the durable Service Bus saga stays exactly as it is, and Event Grid is added *alongside* it for a different class of work. The decision is recorded in [ADR-019](../adr/ADR-019-serverless-notification-functions-eventgrid.md).

### Understand

**Two eventing mechanisms, two jobs.** The platform now runs two transports on purpose, and the distinction is the whole point of this step:

- **Service Bus + the order saga — the durable backbone.** This carries the *transaction-critical* workflow: reserve stock → take payment → confirm. It is **ordered**, **pull-based** (each consumer processes at its own pace), and **guaranteed** — messages are retried and dead-lettered until handled. Correctness of the order depends on every step completing.
- **Event Grid + a serverless Function — fire-and-forget side-effects.** This carries *discrete reactions* that must **not fail or delay** the core transaction — sending a confirmation email is the canonical example. Event Grid **pushes** each event to the Function (the Function is the registered handler), the Function App **scales to zero** when idle, and it is **billed per execution**. A customer's order is correct whether or not the confirmation email is sent.

**The decoupling guarantee.** The side-effect path is wired so that **a failure in Event Grid publishing, or in the Function itself, cannot roll back or block the saga.** Three properties enforce this:

1. The event is published **after** the durable database commit — the order is already `Confirmed` and saved before any side-effect is attempted.
2. The publish goes through a **never-throws** helper (`TryPublishAsync`): any failure is swallowed and logged, returning `false`; the consumer carries on. There is no transactional coupling between the commit and the publish.
3. The Function runs in a **separate process** (the Function App) reached by a push from Event Grid. If it throws, only that one notification is affected; the order remains confirmed.

**Entra authentication, no key.** The Order service publishes to the topic using `DefaultAzureCredential` against the topic's endpoint hostname (non-secret, committed) — there is **no topic access key**. The publisher identity is granted only **EventGrid Data Sender**. The Function, in turn, reaches any Azure resource it needs (Key Vault, an email service) via its **managed identity**. This continues the secret-less posture from the earlier steps.

### Build

- **A reusable fire-and-forget publisher** lives in `AK.BuildingBlocks.Messaging.EventGrid`: `IEventGridSideEffectPublisher` with a single `TryPublishAsync(eventType, subject, data)` whose contract is that **it never throws**. The implementation builds an `EventGridPublisherClient` from the non-secret `EventGrid:TopicEndpoint` setting and `DefaultAzureCredential`; if the endpoint is missing or invalid the publisher becomes a **safe no-op** (logged once at startup), so a service starts cleanly offline. `AddEventGridSideEffectPublisher()` registers it. The package `Azure.Messaging.EventGrid` is added to BuildingBlocks.
- **The publish happens at the natural domain moment.** The existing `OrderConfirmedConsumer` (AK.Order) already updates the order to `Confirmed` and commits; *after* that commit it calls `TryPublishAsync("AntKart.Order.Confirmed", "orders/{id}", …)`. No new consumer, no change to the saga, no change to the Service Bus wiring — the side-effect is bolted on after the durable work is done. `appsettings` gains a non-secret `EventGrid:TopicEndpoint`.
- **The serverless handler** is a new **.NET 9 isolated** Functions project, `AK.Notification.Functions`, deployed to `func-antkart-notifications-dev`. `OrderConfirmedNotificationFunction` is an `[EventGridTrigger]` function that, for now, **records** the side-effect (logs the event); actual email delivery is deferred to the test-enablement step. The project carries heavy teaching comments explaining where it sits in the two-mechanism model and why a failure here is harmless to the order.
- **Tests.** The new behaviour is unit-tested with the publisher mocked (no live Azure): the consumer confirms the order **and** publishes the side-effect; and — proving the decoupling — when the publish returns `false`, the order is **still confirmed** and nothing throws. The transport-agnostic in-memory integration harness registers a no-op publisher so the existing saga/consumer tests stay green.

### Execute

Grant your developer identity the publisher role on the topic and set the non-secret endpoint, then run the Order service so it publishes over Entra after a confirmation:

```bash
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
TOPIC_ID=$(az eventgrid topic show --name evgt-antkart-dev --resource-group rg-antkart-dev-eastus --query id -o tsv)

az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "EventGrid Data Sender" --scope "$TOPIC_ID"
```

The topic endpoint is configured in the Order service's `appsettings.json` (non-secret — committed):

```json
"EventGrid": {
  "TopicEndpoint": "https://evgt-antkart-dev.eastus-1.eventgrid.azure.net/api/events"
}
```

Then run the Order service with an active `az login` session: `cd AK.Order/AK.Order.API && dotnet run`.

### Verify

- The Order service **starts and publishes to Event Grid over Entra** when an order is confirmed — and if the topic is unreachable, the order is **still confirmed** (the publish failure is logged, not thrown).
- The **unit and in-memory integration tests pass** (`dotnet test`) with the publisher mocked — no Event Grid or Function host is required.
- End-to-end delivery — Event Grid **pushing** the event to the deployed Function and the Function sending a real email — is exercised during the **test-enablement step**, not this one.

---

## Step 6 — Resilience and Health Hardening

This step makes the services behave correctly under real cloud conditions — transient faults and throttling — and exposes health endpoints shaped for orchestrator probes. It has two themes: **resilience** (retry the right way, at the right place) and **health** (three probe surfaces, mapped safely). The cross-cutting design is recorded in [RESILIENCE](../guides/resilience-concepts.md) and [OBSERVABILITY](../guides/observability-concepts.md).

### Understand

**Theme A — Resilience.** Cloud dependencies throttle and blip, and the application must absorb that without failing the user request:

- **Cosmos DB throttles.** Cosmos enforces a provisioned-throughput (RU) budget. Exceed it and a request is rejected with **429 — "request rate too large"**, accompanied by a **Retry-After** hint saying how long to wait. The correct response is to **retry, but only after the server's Retry-After window** — retrying sooner spends more of the budget and *deepens* the throttling, a self-inflicted outage. So the data-store retry policy must **honour Retry-After**, not apply a blind/fixed backoff. When there is no hint, it falls back to exponential backoff with jitter (so many instances don't retry in lockstep).
- **Service Bus / Event Grid have transient faults.** A dropped link or a momentary fault should be retried, not surfaced as an error. The Service Bus consumer retry is already configured in MassTransit (`UseMessageRetry`); we keep it and deliberately **do not double-wrap** it with another layer. The Event Grid side-effect publisher (Step 5) already swallows failures (fire-and-forget) and is left as-is.
- **Why retries live at the call site.** The data-access call site knows the operation's idempotency and holds the `CancellationToken`, so it can retry *safely* and abort promptly. A blind, transport-level retry can replay a non-idempotent write or mask a real outage.

**Theme B — Health, and the three probe types.** A health endpoint is not one thing; conflating the three is how a blip becomes an outage:

- **Liveness** = *shallow*. "Is the process responsive?" It must make **no external calls**. A failed liveness probe **restarts the pod** — so if liveness checked Cosmos or Service Bus, one dependency blip would restart every pod at once (a **restart storm**) and turn a momentary blip into a full outage.
- **Readiness** = *shallow or lightly gated, but tolerant*. "Should this pod receive traffic?" A failed readiness probe only **removes the pod from the load balancer**. If readiness failed on a *shared*-dependency blip, **every** pod would drop out together — a fleet-wide **blackout**. So readiness must tolerate transient shared-dependency wobble: dependency checks placed here report **Degraded** (still serving, HTTP 200), not Unhealthy.
- **Deep dependency checks** (Cosmos / Service Bus / Key Vault reachable) belong on a **separate diagnostic endpoint**, for humans and dashboards — **never** wired to liveness/readiness. The diagnostic endpoint is *allowed* to go red without restarting or de-registering anything.

The Kubernetes probes that *consume* these endpoints are wired in the **AKS milestone**; this step **exposes** the endpoints and gets the mapping right.

### Build

- **Resilience (Polly v8, in `AK.BuildingBlocks`).** A reusable `AddDataStoreRetry` pipeline retries a caller-supplied set of transient faults and, via a Polly `DelayGenerator`, **honours a caller-supplied Retry-After** (returning the server's delay verbatim, with no jitter added), falling back to exponential-backoff-with-jitter only when there is no hint. It is **driver-agnostic** — the data-store specifics are passed in as two delegates — so the shared library carries no `MongoDB.Driver` dependency. `AddDataStoreResiliencePipeline(key, …)` registers it as a named pipeline. The Cosmos specifics live in `AK.Products.Infrastructure.Resilience.CosmosResilience`: `IsTransient` (429 / timeout / dropped connection) and `GetRetryAfter` (reads `RetryAfterMs` off the 429 error document). `ProductRepository` runs **every** Cosmos call through the `"cosmos"` pipeline, so the resilience sits exactly at the data-access call site.
- **Health checks (in `AK.BuildingBlocks` + each service).** `AddDefaultHealthChecks()` registers a single shallow `self` check tagged for **both** liveness and readiness, and returns the builder so a service can chain **deep** checks. `MapDefaultHealthChecks()` exposes the three surfaces consistently for every service:
  - `GET /health/live` — only `ak:live`-tagged checks (the shallow `self`); excluded checks never execute, so no dependency is touched.
  - `GET /health/ready` — only `ak:ready`-tagged checks; **Degraded is mapped to HTTP 200** (tolerance), only an explicit Unhealthy returns 503.
  - `GET /health/deps` — **all** checks (self + every deep dependency + MassTransit's own bus check), rendered as detailed JSON. Not a probe target.
  - `GET /health` — kept as a backward-compatible shallow alias.
  Tags use an **`ak:` namespace on purpose**: third-party libraries register their own checks with plain tags (MassTransit tags its bus check `"ready"`), and selecting by `ak:ready` keeps our readiness probe from silently inheriting the bus's state. The deep checks — `MongoDbHealthCheck` (a real `{ ping: 1 }` to Cosmos) and the reusable `KeyVaultHealthCheck` (lists secret *metadata*, never values) — are tagged `ak:deep`, so they surface only on `/health/deps`. **AK.Products** demonstrates the full set; Service Bus reachability is surfaced platform-wide by MassTransit's bus check on `/health/deps`.

### Execute

Everything here is non-secret configuration; the endpoints need no special setup. Run a service locally and hit the three surfaces:

```bash
cd AK.Products/AK.Products.API && dotnet run    # → http://localhost:5077

curl -i http://localhost:5077/health/live       # shallow: 200 "Healthy" whenever the process is up
curl -i http://localhost:5077/health/ready      # tolerant readiness: 200 unless the app truly can't serve
curl -s http://localhost:5077/health/deps | jq  # diagnostics: per-dependency JSON (Cosmos, Key Vault, bus)
```

> **PowerShell note.** These are bash examples; in native PowerShell `curl` aliases `Invoke-WebRequest`, so use **`curl.exe`** for the `-i`/`-s` flags (see [Operations Command Reference → Gotchas](operations-command-reference.md#j-gotchas-and-powershell-notes)).

### Verify

- **Liveness stays shallow:** `/health/live` returns 200 even while a dependency (Cosmos / Key Vault) is down — it makes no external calls, so a dependency outage can never trigger a restart storm.
- **Readiness is tolerant:** `/health/ready` keeps returning 200 through a transient shared-dependency wobble (a dependency check there reports Degraded, not Unhealthy), so the fleet is not pulled out of rotation together.
- **Deep diagnostics are honest and isolated:** `/health/deps` reports each dependency's real state (and may return 503 when one is down) **without** affecting liveness or readiness.
- **Cosmos throttling is respected:** unit tests prove the data-store pipeline retries on a simulated 429 and **waits the server's Retry-After** (not the base delay), retries other transient faults, and does **not** retry logical errors. The Service Bus consumer retry remains the single MassTransit `UseMessageRetry` (no double-wrapping).
- **Build and tests are green** (`dotnet build` / `dotnet test`) with no live Azure required — the resilience and health-registration tests use mocks/abstractions.

---

## Step 7 — Provision Azure Communication Services for Email

The platform sends transactional email (order confirmations, receipts, cancellation notices). This step **provisions the email infrastructure** — Azure Communication Services (ACS) Email — as code, then **wires the application's send path** to it. The infrastructure module and its live unit follow the established Terraform/Terragrunt pattern (see the [Infrastructure Guide](infrastructure-guide.md) §15); the application side adds a reusable email sender and connects the notification paths (covered under *Application email sending* below).

### Understand

**ACS Email** is Azure's managed service for sending application email — no self-hosted SMTP server and no third-party email vendor to contract. It needs three pieces plus a link: an **Email Communication Service** (owns email domains), an **email domain** (the sender identity), a **Communication Service** (what the app connects to), and a **domain association** (authorises the service to send "from" the domain).

**Why an Azure-managed domain — no DNS work.** The email domain is created with `domain_management = "AzureManaged"`, so Azure provisions a **free `*.azurecomm.net` sender subdomain and auto-configures its SPF/DKIM**. There is **no custom domain to buy and no DNS records to publish** — ideal for a non-production platform. (A customer-managed domain would require owning a domain and publishing verification/DKIM/SPF records.)

**The app sends via managed identity — no key.** ACS exposes connection strings and access keys, but the application authenticates to the Communication Service endpoint with its Microsoft **Entra managed identity** (an Azure AD token), granted a data-plane role on the service in the role-assignment step. So **no key or connection string is output, stored, or committed** — consistent with the platform's secret-less posture.

### Build

A reusable **`communication-services` module** (`variables.tf` / `main.tf` / `outputs.tf`, heavy teaching comments) and its **dev live unit** (`environments/dev/communication-services/terragrunt.hcl`, with the resource-group dependency and committed lock file). The module creates the four resources above; its outputs expose the **endpoint** (`https://<hostname>`) and the **MailFrom sender address** (`DoNotReply@<managed-subdomain>`) for the application code to consume — and deliberately **no** keys/connection strings.

### Execute

```powershell
cd infrastructure/environments/dev/communication-services
terragrunt init      # generates backend/provider, resolves the resource-group dependency
terragrunt plan      # expect 4 to add, 0 to change, 0 to destroy
terragrunt apply
```

### Verify

```powershell
$RG = "rg-antkart-dev-eastus"
az communication show --name "acs-antkart-dev" --resource-group $RG `
  --query "{name:name, hostName:hostName, provisioningState:provisioningState}" -o json
az communication email domain show `
  --email-service-name "acs-email-antkart-dev" --name "AzureManagedDomain" --resource-group $RG `
  --query "{fromSenderDomain:fromSenderDomain, dataLocation:dataLocation}" -o json
```

A correct result: `terragrunt apply` ends with `4 added, 0 changed, 0 destroyed`; the Communication Service reports `provisioningState = Succeeded` and a `hostName`; and the managed domain reports a `*.azurecomm.net` `fromSenderDomain`. **No keys** are output — the application reaches the endpoint with its managed identity (next).

### Application email sending

**A reusable sender.** `AK.BuildingBlocks.Email` adds an `IEmailSender` abstraction and an `AcsEmailSender` built on the **`Azure.Communication.Email`** SDK. The notification paths depend on the interface, not the provider. `AddAcsEmailSender(configuration)` wires it from the non-secret `Acs` config section (`Acs:Endpoint`, `Acs:SenderAddress`, optional `Acs:SenderDisplayName`).

**Entra-first authentication (secret-less), with a Key-Vault fallback.** The `EmailClient` is constructed once and selected as follows:

- **Default path — managed identity:** `new EmailClient(new Uri(Acs:Endpoint), new DefaultAzureCredential())`. This is the SDK's Entra constructor. It authenticates with the app's **managed identity** in the cloud (and the developer's `az login` locally) — no secret. For this to work, the identity must hold the **Communication and Email Service Owner** role **scoped to that single ACS resource** (assigned in the role-assignment step) — the data-plane role that authorizes sending email, granted at the narrowest scope.
- **Fallback — connection string:** if `Acs:ConnectionString` is present, `new EmailClient(connectionString)` is used instead. The connection string is a **secret**; it is **never committed** and, if ever needed (an environment where Entra-based send is unavailable), is supplied from **Key Vault**. Only `Acs:Endpoint` and `Acs:SenderAddress` live in committed config.
- If neither is configured (e.g. local dev), the sender is a **safe no-op** (logs and returns) — it never faults the caller.

**Managed sender domain + friendly display name.** Mail is sent from the Azure-managed `DoNotReply@<managed-subdomain>.azurecomm.net` address (the `sender_address` output). The optional `Acs:SenderDisplayName = "AntKart"` is applied as the RFC 5322 `"AntKart <DoNotReply@…>"` form, so recipients see a friendly From name.

**Where it's wired.** The Event Grid-triggered Function (`AK.Notification.Functions`) composes and sends the order-confirmation email via `IEmailSender` (authenticating through the Function App's managed identity) instead of just logging; the reusable core it dispatches through (`AK.Notification.Core`) sends through the same `IEmailSender` (its `EmailNotificationChannel` delegates to it), replacing the previous local SMTP/Mailhog path. **Decoupling is preserved:** the Function wraps the send in try/catch and the dispatcher records a failed send rather than throwing — a mail error never faults the function or the (already-committed) order.

**Verifying a send.** Email delivery is exercised with **controlled test-recipient addresses set via test data** (never real customers) — point an order's `customerEmail` at a mailbox you own and confirm receipt. In tests, the `EmailClient`/`IEmailSender` is mocked, so no live Azure is required.

### Product seed data — a committed dataset and an idempotent loader

To run realistic end-to-end tests, the catalogue needs realistic data. This sub-step adds a **committed seed dataset** and a **secret-less, idempotent loader**.

**The dataset (`AK.Seed-Data/products.csv`).** A **deterministic generator** (`AK.Tools.ProductsSeedGenerator`) produces **3,000 products** — 1,000 each across Men / Women / Kids, with 10 realistic subcategories each spanning apparel, footwear and accessories — with realistic names, brands, prices, sizes, colours, stock, and a **unique SKU** per product. The generator uses a fixed RNG seed and a fixed iteration order, so regenerating yields **byte-identical output**. The CSV is plain data (not a secret) and is **committed** alongside the generator. Its columns mirror the `AK.Products` domain model exactly; list fields (`Sizes`, `Colors`) are pipe-delimited; there is **no id column**.

**Idempotent-by-SKU design.** The loader (`AK.Tools.ProductsSeedLoader`) derives each document's `_id` **deterministically from the SKU** (an MD5 of the SKU → a 32-hex string, the same shape as the default GUID id). Because the `products` collection is sharded on `{ "_id": "hashed" }`, the upsert filters on `_id` — a **single-partition point write**. The same SKU therefore always maps to the same document, so the load is **idempotent**: re-running converges to exactly 3,000 products and **never creates duplicates** (`ReplaceOneAsync` with `IsUpsert = true`). The domain exposes a `Product.CreateForSeed(id, …)` factory so the loader can assign that deterministic id; mapping/Mongo code is **reused** from the Products persistence layer (not duplicated).

**Secret-less connection.** The loader reuses the **same configuration foundation** as the Products service: non-secret `MongoDbSettings` (database + collection) from `appsettings.json`, and the Cosmos connection string resolved from **Key Vault** via `DefaultAzureCredential` — no secret in the tool or the repo.

**Run it** (with an identity holding Key Vault Secrets User + Cosmos access; safe to run repeatedly):

```bash
# (re)generate the CSV — optional, it is already committed
dotnet run --project AK.Tools/AK.Tools.ProductsSeedGenerator   # writes AK.Seed-Data/products.csv (3,000 rows)

# upsert the catalogue into Cosmos
dotnet run --project AK.Tools/AK.Tools.ProductsSeedLoader       # logs progress; "Upserted 3000 products (total)."
```

Verify by re-running the loader: the product count stays at 3,000 (idempotent). The CSV parsing and the upsert logic are unit-tested with a mocked sink, so **no live Cosmos is required** to test the loader.

### Payments — Razorpay sandbox credentials from Key Vault

The **AK.Payments** service calls the Razorpay sandbox, which needs a key id + key secret. Those are **secrets**, so — exactly like the Products Cosmos connection string (Step 1) — they are read from **Key Vault**, never committed.

- **Wiring.** `AK.Payments.API` startup now calls the same `builder.Configuration.AddAzureKeyVaultConfiguration(...)` BuildingBlocks extension that Products uses: conditional on the non-secret `KeyVault:Uri` setting and authenticated with `DefaultAzureCredential` (the developer's `az login` locally, the service's managed identity in the cloud — no secret).
- **How the secrets flow.** Key Vault secrets named **`Razorpay--KeyId`** and **`Razorpay--KeySecret`** are projected by the configuration provider into `Razorpay:KeyId` / `Razorpay:KeySecret`, which bind to the existing `RazorpaySettings`. (Key Vault's `--` secret-name separator maps to the configuration `:` hierarchy.)
- **Nothing committed.** The `Razorpay` section in `appsettings.json` / `appsettings.Production.json` holds only empty strings with a note that the values are vaulted; the real sandbox keys exist only in Key Vault. Store them once with, e.g., `az keyvault secret set --vault-name kv-antkart-dev --name "Razorpay--KeyId" --value "<sandbox key id>"` (and `Razorpay--KeySecret`).

Existing Payments tests mock `IConfiguration` / `RazorpaySettings`, so they are unaffected; no host is booted in tests, so no live Key Vault call is made.

### Notification — reusable core for the serverless model

Notifications run on a pure serverless model (Event Grid + Azure Functions; see Step 5 and [ADR-019](../adr/ADR-019-serverless-notification-functions-eventgrid.md)). The reusable core (`AK.Notification.Core`), the serverless Functions, and the **owning-service publishers** are all in place. The old AK.Notification REST host and its Service Bus consumers — together with the `notification` subscription on the integration-events topic — have since been removed; notifications no longer consume Service Bus.

**Owning services publish the events (commit-then-notify).** Order and Payments emit the shared `NotificationEvents` contracts to Event Grid as **fire-and-forget side-effects via `IEventGridSideEffectPublisher.TryPublishAsync` (never-throws), strictly AFTER the durable business commit** — never inside the transaction. A notification publish failure can never roll back or fail the business operation.
- **Order:** `OrderCreated` (CreateOrderCommandHandler, after the order+outbox commit); `OrderConfirmed` (OrderConfirmedConsumer, now using the shared contract instead of an ad-hoc shape); `OrderCancelled` (OrderCancelledConsumer — the single sink for both the customer-initiated and saga-driven cancel paths, so exactly one notification per cancellation).
- **Payments:** `PaymentSucceeded` / `PaymentFailed` (VerifyPaymentCommandHandler, after the payment-outcome commit). `customerEmail` is already a field on the `Payment` entity (captured at InitiatePayment from the order context), so it is available at the publish point with no new lookup.

- **Reusable core:** `AK.Notification.Core` — framework-agnostic, one call: `AddNotificationCore(configuration)`.
  - **Channel abstraction (extension point):** `NotificationChannelType` (Email now; WhatsApp/Sms placeholders) + `INotificationChannel` returning a `NotificationSendResult`; `EmailNotificationChannel` wraps the shared ACS `IEmailSender`. Adding a channel is Open/Closed — implement + register; no dispatcher/template/caller changes.
  - **Templates:** one per `NotificationType`, resolved by type, composing subject + HTML + plain-text.
  - **Dispatcher:** `INotificationDispatcher` resolves the template → composes the message → sends on each target channel (default Email) → writes one **history audit row per attempt** (Sent/Failed). It **never throws** for a delivery failure — it records it, so notification stays a best-effort side-effect.
  - **History (EF adapter, surviving):** `NotificationHistory` audit trail (id, notificationType, recipient, channelType, status, timestamp, correlationId, error) with an EF Core `NotificationHistoryDbContext` + `InitialCreate` migration in Core — secret-less connection (`ConnectionStrings:Notifications`, Key-Vault-sourced), same pattern as the other services. It lives in Core (a surviving project), not the old Infrastructure.
- **Shared event contracts (single definition):** `AK.BuildingBlocks.Messaging.Notifications` — `NotificationEventTypes` (Event Grid `eventType` constants, e.g. `AntKart.Order.Confirmed`) + five payload records. Referenced by both the Functions (consume) and Order/Payments (publish, next step), so they never drift.
- **Functions (`AK.Notification.Functions`):** one thin `[EventGridTrigger]` per event (`OnOrderCreated` … `OnPaymentFailed`). Each only deserializes the contract → builds a `NotificationRequest` → calls the dispatcher; all logic stays in the core. `Program.cs` folds in Key Vault (secret-less) and calls `AddNotificationCore`. A malformed payload is logged and skipped.

Everything is unit-tested with EF InMemory + mocks (templates, email channel, dispatcher send/persist/failure, EF history persist, and each Function's deserialize→dispatch) — **no live Azure or database required**.

---

## Step 8 — Serverless Notification Proven End to End

The pieces built in Steps 5 and 7 — the Event Grid publisher, the Functions, the ACS email sender — are now proven **working together against live Azure**: an order raises a domain event that travels through Event Grid to a Function and out as a real email via Azure Communication Services. This step records the exact runtime configuration and the two managed-identity role assignments that path depends on, so it can be reproduced.

### Understand

**The path.** `Order → Event Grid custom topic → Azure Functions (Event Grid trigger) → Azure Communication Services email`. The Order service publishes a notification event as a fire-and-forget side-effect **after** its durable commit; Event Grid **pushes** the event to the subscribed Function; the Function dispatches through `AK.Notification.Core`, which sends the email via ACS. Each hop authenticates with a **managed identity** — there is no key or connection string anywhere on the path.

**Two identities, two grants.** The path crosses two resource boundaries, so exactly two data-plane role assignments are required:

- The **publisher** (the Order/Payments service identity) needs **EventGrid Data Sender** on the custom topic — permission to publish events.
- The **sender** (the Function App's identity) needs **Communication and Email Service Owner** on the ACS resource — permission to send email.

### Build / Configure

The Function reads two non-secret ACS settings (the same `Acs` section the reusable sender binds, from Step 7):

- **`Acs:Endpoint`** — the Communication Service endpoint (`https://<hostname>`), the resource the Function authenticates to with its managed identity.
- **`Acs:SenderAddress`** — the MailFrom address the email is sent from. This value **must exactly match a provisioned MailFrom address on the linked Azure Managed Domain** (the `DoNotReply@<managed-subdomain>.azurecomm.net` sender created in Step 7). A mismatch is rejected by ACS at send time — the address must be one the domain is authorised to send from.

The recipient is **derived from the signed-in user's `email` claim** (configured in Step 2c), not supplied by the client.

### Execute

Grant the two roles (replace the resource group with the dev value), then exercise the flow by placing an order whose signed-in user has a deliverable email:

```bash
RG=rg-antkart-dev-eastus

# 1. Publisher identity → EventGrid Data Sender on the topic
TOPIC_ID=$(az eventgrid topic show --name evgt-antkart-dev --resource-group $RG --query id -o tsv)
az role assignment create --assignee-object-id "<publisher-identity-object-id>" --assignee-principal-type ServicePrincipal \
  --role "EventGrid Data Sender" --scope "$TOPIC_ID"

# 2. Function App identity → Communication and Email Service Owner on the ACS resource
ACS_ID=$(az communication show --name acs-antkart-dev --resource-group $RG --query id -o tsv)
az role assignment create --assignee-object-id "<function-app-identity-object-id>" --assignee-principal-type ServicePrincipal \
  --role "Communication and Email Service Owner" --scope "$ACS_ID"
```

The Function's ACS settings are non-secret and set in its application configuration:

```json
"Acs": {
  "Endpoint": "https://acs-antkart-dev.<region>.communication.azure.com",
  "SenderAddress": "DoNotReply@<managed-subdomain>.azurecomm.net"
}
```

### Verify

- Placing an order results in a **received email** at the user's address — the event was published to Event Grid, pushed to the Function, and delivered through ACS.
- A `NotificationHistory` audit row records the attempt (Sent / Failed).
- **Decoupling holds:** if the email cannot be sent, the Function records a failed send rather than throwing, and the order remains committed — a notification failure never affects the order.

---

*Subsequent steps are added to this guide as they are delivered.*
