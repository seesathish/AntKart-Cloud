# Full-cloud end-to-end

This is the whole platform exercised as a user would, through its public HTTPS entry point — the ingress in front of the gateway at **`https://api.antkart.in`**. The internal services are `ClusterIP`-only, so every call goes through the `/gateway/*` routes. TLS is terminated by a trusted Let's Encrypt **production** certificate, so no verification needs disabling. Two journeys matter: the **positive path** an order takes to completion, and the **compensation path** the SAGA takes when a step fails.

> **Only cloud counts.** This journey is valid **only** against `https://api.antkart.in` and the managed services behind it. A localhost/Docker-Compose run is superseded — see the [Testing index](README.md).

## Before you start

- A delegated Entra token in the `Authorization: Bearer` header for each call — see [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md). Two distinct test users let you prove cross-user isolation.
- The **`AntKart Cloud E2E Saga`** Postman collection (`AntKart-Cloud-E2E-Saga-Positive.postman_collection.json` at the repo root), targeting the gateway routes. Run it in the **Collection Runner** with an 8000 ms inter-request delay — the saga is asynchronous and steps self-retry. Auth is collection-level OAuth 2.0 Authorization Code + PKCE against Entra; fill `entraClientId` (the public-client app registration id) and `razorpayKeySecret` (from Key Vault) before the first run. The external-to-internal route mapping is in the [Testing index](README.md#cluster-end-to-end-verification-public-ingress).

## Positive path — order to completion

1. **Health** — `GET /gateway/health/{products|cart|orders|payments}` returns 200 for each backing service; the gateway's own `/health/live` and `/health/ready` return 200.
2. **Browse** — `GET /gateway/products` (and `/gateway/products/{id}`) returns the catalogue from Cosmos DB.
3. **Add to cart** — `POST /gateway/cart/items`, then `GET /gateway/cart` returns the signed-in user's cart from Redis.
4. **Create order** — `POST /gateway/orders` drives, in one call: server-authoritative **price revalidation** against the catalogue, then the orchestrated **SAGA** — stock reservation → order confirmation — followed by cart clearing. Two notification emails (order created, order confirmed) are delivered via **Event Grid → Functions → ACS**.
5. **Pay** — `POST /gateway/payments/initiate`, then `POST /gateway/payments/verify` with the Razorpay signature; on success the order is driven to its paid state and a payment-succeeded email is delivered.

**Pricing is server-authoritative.** The order is always priced from the catalogue. A submitted line price **below** the catalogue price is not honoured as a discount — because the catalogue price is higher, it is treated as a **price increase** and rejected with `409 PriceChanged`. Equal or a submitted price above the catalogue is accepted and charged the catalogue price; a missing/inactive product returns `422`; an unreachable catalogue fails closed with `503` and nothing is persisted.

The mechanics behind this path — the outbox, the saga orchestration, domain vs integration events — are described in [Platform architecture](../development/0-platform-architecture.md).

## Compensation path — when a step fails

The SAGA is designed to unwind cleanly. Two failures are worth driving explicitly:

- **Stock reservation fails** (order a product with no available stock) — the SAGA publishes the reservation-failure event, the order is moved to `Cancelled`, and an order-cancelled email is delivered. Verify the order status and the email.
- **Payment fails** (verify with an invalid signature) — the payment-failed event is consumed by Order, the order moves to `PaymentFailed`, and a payment-failed email is delivered.

**Known limitation:** when a payment fails, stock already reserved by the SAGA is **not** released today — a deliberate deferral tracked as [KI-005](../KNOWN_ISSUES.md). See [Platform architecture — Open items](../development/0-platform-architecture.md).

The state machine that governs which order transitions are legal (and which are rejected with `409`) is described with the [Order design](../../AK.Order/ORDER_TECHNICAL_DESIGN.md).

## Data — confirm what the services persisted

Behind the endpoints, verify the managed stores hold the expected state — reach them with `kubectl port-forward` (cluster) or a run against the live service, always secret-less via workload identity ([Kubernetes](../development/3-kubernetes.md), [Security](../development/6-security.md)):

- **Products** — the catalogue is served from Cosmos DB (MongoDB API); reads return seeded products and re-seeding is idempotent ([Products design](../../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md)).
- **Order / Payments / Discount** — PostgreSQL holds orders, payments, and coupons; an order's status transitions and a payment's outcome persist after the saga settles.
- **Cart** — Azure Managed Redis holds the per-user cart under `AKCart:cart:{userId}` with a 30-day TTL.
- **Notification history** — each dispatch writes an audit row via the serverless Core ([Serverless & Eventing Concepts](../guides/serverless-eventing-concepts.md)).

## Related

- Security testing (being rebuilt — see the Roadmap): [Security Testing](SECURITY_TESTS.md).
- The automated code-level baseline (`dotnet test` — layer-agnostic unit + integration suites in CI) and the full strategy and gateway route table: [Testing index](README.md).
