# Testing

This is the single entry point for AntKart's verification strategy. It indexes every test type, from code-level checks to end-to-end and security validation, so a reviewer can assess coverage from one place.

The platform is verified at every layer. **Unit tests** confirm domain logic, validators, and handlers in isolation. **Integration tests** verify the orchestrated SAGA and event-bus flows on an in-memory transport. **End-to-end tests** exercise the running services through their public surface. **Security tests** probe authentication, authorization, and input handling. **Load and performance tests** confirm behaviour under high-volume transaction throughput against cloud services. The unit and integration suites are layer-agnostic — they run identically regardless of where the services are deployed — while the end-to-end, security, and performance tests run against running services and grow with the deployment topology.

> **⛔ Local/localhost testing is SUPERSEDED.** Only tests executed **against cloud resources through the public HTTPS endpoint `https://api.antkart.in`** validate the delivered platform. The unit and in-memory integration suites (`dotnet test`) remain valid as layer-agnostic code checks and run in CI. Any manual end-to-end / security procedure that targets `localhost` or a local Docker Compose stack does **not** exercise the cloud platform and its results are not valid; the Phase-1 local manual guide has been retired. The full-cloud path below — driven by the `AntKart Cloud E2E Saga` Postman collection — is the valid one; a Load/Performance guide is planned (see the [Roadmap](../ROADMAP.md)).

---

## Unit Tests

Per-project automated unit tests covering domain logic, validators, command and query handlers, mappers, and consumers — written with **xUnit**, **Moq**, and **FluentAssertions**. They have no external dependencies (no database, broker, or network) and run on any machine or pipeline.

```bash
dotnet test
```

| Project | Tests |
|---------|-------|
| AK.Products.Tests | 218 |
| AK.Order.Tests | 144 |
| AK.ShoppingCart.Tests | 88 |
| AK.Payments.Tests | 73 |
| AK.Discount.Tests | 49 |
| AK.Notification.Tests | 23 |
| AK.BuildingBlocks.Tests | 10 |
| AK.Tools.ProductsSeedLoader.Tests | 11 |
| AK.Tools.DiscountSeedLoader.Tests | 12 |
| **Unit subtotal** | **628** |

With the **28** integration tests below, the full automated suite is **656 tests** (all passing).

---

## Integration Tests

The `AK.IntegrationTests` suite (**28 tests**) exercises the orchestrated SAGA, event-bus flows, and payment event routing using **MassTransit's in-memory test harness** — no broker, no database, and no running host. It validates the messaging contracts and orchestration logic deterministically and in isolation.

Detail: [AK.IntegrationTests/INTEGRATION_TESTS.md](../../AK.IntegrationTests/INTEGRATION_TESTS.md).

---

## End-to-End / Functional Tests

[Full-cloud end-to-end](1-full-cloud-end-to-end.md), driven by the **`AntKart Cloud E2E Saga`** Postman collection (`AntKart-Cloud-E2E-Saga-Positive.postman_collection.json`), walks every service end-to-end through its public surface — the positive order path, the SAGA compensation path, persisted-data checks, and notification delivery.

To call the APIs you need a token. For the interactive sign-in that obtains a delegated user token from Entra ID via Postman (OAuth2 Authorization Code + PKCE), and the most common pitfalls (the audience claim and 401s), see [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md).

**Full-cloud verification via the ingress is the only valid end-to-end path** — the platform is exercised through the public HTTPS ingress at **`https://api.antkart.in`**, detailed in the section below. The former Phase-1 local (Docker Compose) manual walkthrough has been retired.

---

## Cluster End-to-End Verification (public ingress)

The platform is verified against the **cluster** through its public HTTPS entry point — the ingress in front of the gateway — using a Postman collection that targets the **gateway routes** (not the individual services, which are internal `ClusterIP`). The base URL is the custom domain **`https://api.antkart.in`** (GoDaddy A record → the ingress public IP), which terminates TLS with a **trusted Let's Encrypt production certificate** — no need to disable TLS verification in Postman.

The verified journey runs the **full orchestrated saga to its `Paid` terminal state** (the **`AntKart Cloud E2E Saga`** Postman collection, `AntKart-Cloud-E2E-Saga-Positive.postman_collection.json`), then the **payment-failure branch** to `PaymentFailed`:

1. **Health** — `GET /gateway/health/{products|cart|orders|payments}` returns 200 for each backing service; the gateway's own `GET /health/live` and `/health/ready` return 200.
2. **Browse** — `GET /gateway/products` (and `/gateway/products/{id}`) returns the catalogue.
3. **Add to cart** — `POST /gateway/cart/items`, then `GET /gateway/cart` returns the current user's cart.
4. **Create order** — `POST /gateway/orders` drives server-authoritative price revalidation and starts the orchestrated SAGA. The order is created `Pending`; the `OrderCreated` notification email is sent.
5. **Get order — expect `Confirmed`** — `GET /gateway/orders/{id}`. On successful stock reservation the saga advances the order `Pending → Confirmed`.
6. **Initiate payment** — `POST /gateway/payments/initiate` for the order returns a `razorpayOrderId` (the outbound Razorpay egress leg) and persists a pending `Payment` (capturing `customerEmail`).
7. **Pay on Razorpay (sandbox)** — complete the checkout with a test card (Visa `4111 1111 1111 1111`, OTP `1234 1234`); Razorpay returns the payment id and signature.
8. **Verify payment** — `POST /gateway/payments/verify` with the Razorpay order id, payment id, and signature. The HMAC signature is verified locally, the `Payment` moves to `Succeeded`, and `PaymentSucceeded` is published — both as the integration event the saga consumes and as the `PaymentSucceeded` notification to Event Grid.
9. **Saga applies the outcome** — the order saga consumes `PaymentSucceeded` and transitions the order `Confirmed → Paid` (the transition added in [KI-009](../KNOWN_ISSUES.md)).
10. **Cart cleared / notifications** — the cart was cleared on order confirmation (`GET /gateway/cart` returns empty); the `OrderConfirmed` and `PaymentSucceeded` emails are delivered via **Event Grid → Functions → ACS**.
11. **Get order — expect `Paid`** — `GET /gateway/orders/{id}` shows `Paid`, the positive terminal state.

**Payment-failure branch.** Repeat steps 4–8 with a payment that fails verification (a declined test card or a deliberately invalid signature): `verify` returns failure, the `Payment` moves to `Failed`, `PaymentFailed` is published, and the saga transitions the order `Confirmed → PaymentFailed` (the other transition added in [KI-009](../KNOWN_ISSUES.md)); a `PaymentFailed` notification email is sent. `GET /gateway/orders/{id}` then shows `PaymentFailed`. Note the stock reserved earlier is **not yet released** on this path — a known, deferred compensation gap ([KI-005](../KNOWN_ISSUES.md)).

Calls need a delegated Entra token in the `Authorization: Bearer` header (see [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md)).

**Pricing is server-authoritative.** The order is always priced from the catalogue. A submitted line price **below** the catalogue price is not honoured as a discount — because the catalogue price is higher, it is treated as a **price increase** and the order is rejected with `409 PriceChanged` (equal or a submitted price above the catalogue is accepted and charged the catalogue price; a missing/inactive product returns `422`; an unreachable catalogue fails closed with `503`).

### Gateway route mapping

External clients call the **gateway upstream** path; the gateway rewrites it to the service's own `/api` path over cluster DNS. Discount is internal-only (gRPC, called by Products) and has **no** gateway route.

| Service | Service API path (internal) | Gateway upstream path (external) |
|---------|-----------------------------|----------------------------------|
| Products | `GET/POST /api/v1/products`, `.../api/v1/products/{id\|categories\|featured\|category/{c}}` | `/gateway/products`, `/gateway/products/{everything}` |
| ShoppingCart | `GET/DELETE /api/v1/cart`, `POST/PUT/DELETE /api/v1/cart/items/...` | `/gateway/cart`, `/gateway/cart/{everything}` |
| Order | `GET/POST /api/orders`, `.../api/orders/{me\|{id}\|{id}/status}` | `/gateway/orders`, `/gateway/orders/{everything}` |
| Payments | `/api/payments/{initiate\|verify\|me\|{id}\|order/{id}}`, `/api/payments/cards...` | `/gateway/payments/{everything}` |
| Per-service health | `GET /health` | `GET /gateway/health/{products\|cart\|orders\|payments}` |
| Gateway (own) | `GET /health/live`, `/health/ready` | served directly at the ingress root (`/health/*`) |

Both a **bare** route (`/gateway/products`, `/gateway/cart`, `/gateway/orders`) and an `{everything}` route exist wherever a service exposes an endpoint with no trailing segment (e.g. `GET /api/v1/cart`); Payments has no bare `/api/payments` endpoint, so only its `{everything}` route is defined.

---

## Security Tests

[Security testing](SECURITY_TESTS.md) is being **rebuilt** — the detailed procedures were cleared pending the planned security work (the Security / ethical-hacking guide and the broader Security programme on the [Roadmap](../ROADMAP.md)). The boundaries enforced today are described in [Security — how it is secured](../development/6-security.md); the automated `dotnet test` suites already cover authorization, ownership, and input-validation rules at the handler level.

---

## Load / Performance Tests

High-volume transaction testing will validate throughput, latency, and resilience under sustained load against the managed cloud services (data stores, messaging, and the gateway), confirming the platform's scaling and circuit-breaking behaviour.

**(To be added)** — the load and performance test guide follows the cloud deployment and Kubernetes phases, once the managed services and ingress are in place.
