# AK.Gateway — API Gateway Technical Design

> **⛔ SUPERSEDED — describes the Phase-1 platform running locally on Docker Compose.** Retained for historical reference. For the current cloud-native platform, see the [README](../README.md) and the [Development Guide](../DevelopmentGuide.md).

## Overview

The API Gateway is the single entry point for AntKart's REST services. Built with **Ocelot 23.4.2** on .NET 9, it provides routing, edge JWT validation, per-route rate limiting, and circuit-breaker QoS — without any business logic. In the cluster it runs as `ak-gateway` (ClusterIP on port 8080) and is the only service exposed externally, through the cluster ingress.

---

## Architecture Position

```
Client (browser / mobile / Postman)
          │  HTTPS
          ▼
   Cluster ingress  (TLS termination)
          │  HTTP
          ▼  :8080
  ┌───────────────────┐
  │    ak-gateway     │  Ocelot + Entra JWT validation
  └───────────────────┘
    │         │         │          │
    ▼         ▼         ▼          ▼
ak-products  ak-cart  ak-order  ak-payments
  :8080       :8080    :8080      :8080
```

AK.Discount (gRPC over h2c) is an internal dependency called by AK.Products; it is **not** routed by the gateway and is never exposed externally.

---

## Project Structure

```
AK.Gateway/
└── AK.Gateway.API/
    ├── AK.Gateway.API.csproj
    ├── Program.cs
    ├── Dockerfile
    ├── appsettings.json
    ├── appsettings.Production.json
    ├── ocelot.json                   ← in-cluster ak-* Service routes (default; also supplied by the Helm ConfigMap)
    └── ocelot.Development.json       ← localhost routes for local runs
```

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Ocelot | 23.4.2 | API gateway routing, rate limiting, QoS |
| Ocelot.Provider.Polly | 23.4.2 | Polly-backed QoS (circuit breaker + timeout) per route |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.0 | JWT bearer validation |
| Serilog.AspNetCore | 8.0.3 | Structured logging |
| AK.BuildingBlocks | local | Entra authentication, health checks, correlation middleware |

---

## Route Configuration

`ocelot.json` defines **11** upstream → downstream route mappings. External callers use the `/gateway/*` **upstream** path; Ocelot rewrites it to the service's own `/api` **downstream** path on the in-cluster Service. All proxy routes require a Bearer token; the health routes are anonymous.

| Upstream (Gateway) | Downstream | Rate limit | Auth |
|--------------------|------------|------------|------|
| `/gateway/products`, `/gateway/products/{everything}` | `ak-products:8080` → `/api/v1/products[...]` | 20 rps | Bearer |
| `/gateway/cart`, `/gateway/cart/{everything}` | `ak-cart:8080` → `/api/v1/cart[...]` | 10 rps | Bearer |
| `/gateway/orders`, `/gateway/orders/{everything}` | `ak-order:8080` → `/api/orders[...]` | 10 rps | Bearer |
| `/gateway/payments/{everything}` | `ak-payments:8080` → `/api/payments/...` | 30 rps | Bearer |
| `/gateway/health/{products\|cart\|orders\|payments}` | respective service → `/health` | — | None |

Payments has no bare `/api/payments` endpoint, so only its `{everything}` route is defined. The gateway's own health endpoints (`/health`, `/health/live`, `/health/ready`, `/health/deps`) are served directly by the gateway, not proxied. The same route set is supplied to the deployed pod by the Helm ocelot ConfigMap.

### Rate limiting

Per-route, backed by a memory-cache store (`AddMemoryCache`): Products 20 rps, Cart 10 rps, Orders 10 rps, Payments 30 rps. Exceeding a limit returns `429`.

```json
"RateLimitOptions": { "EnableRateLimiting": true, "Period": "1s", "PeriodTimespan": 1, "Limit": 20 }
```

### QoS (circuit breaker)

A per-route Polly circuit breaker and timeout. `DurationOfBreak` is 30 s and `TimeoutValue` 10 s throughout; `ExceptionsAllowedBeforeBreaking` is 5 for Products/Payments and 3 for Cart/Orders.

```json
"QoSOptions": { "ExceptionsAllowedBeforeBreaking": 5, "DurationOfBreak": 30000, "TimeoutValue": 10000 }
```

---

## Authentication Strategy

JWT is validated **twice** (defence in depth):

1. **At the gateway** — Ocelot checks `AuthenticationOptions.AuthenticationProviderKey: "Bearer"` on each proxy route and rejects unauthenticated requests with `401` before forwarding.
2. **At the downstream service** — each service independently validates the same JWT, so it remains secure even if reached directly (bypassing the gateway).

The `Authorization` header is forwarded verbatim; Microsoft Entra ID tokens flow through unchanged.

---

## Program.cs Bootstrap

```csharp
builder.AddSerilogLogging();
builder.Services.AddEntraAuthentication(builder.Configuration);   // Entra JWT bearer at the edge
builder.Services.AddDefaultHealthChecks();
builder.Services.AddMemoryCache();                                // required by Ocelot's rate limiting
builder.Services.AddOcelot(builder.Configuration).AddPolly();     // routing + Polly QoS
builder.Services.AddCors(/* AllowAll in dev */);

app.UseCors("AllowAll");
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseEntraAuth();
app.MapDefaultHealthChecks();

// Ocelot's middleware is TERMINAL, so it runs only for non-/health paths; /health/*
// falls through to the mapped health endpoints (otherwise the probe would 404 and
// restart-loop the pod).
app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/health"),
    ocelotApp => ocelotApp.UseOcelot().GetAwaiter().GetResult());
```

The Ocelot routing config is loaded from **exactly one** file selected by environment (see [Development Overrides](#development-overrides)); merging `ocelot.json` and `ocelot.Development.json` would duplicate routes and Ocelot would refuse to start.

---

## Container and Exposure

- **Listens on:** port **8080** in the container (non-root user; the .NET base image default). There is no docker-compose host port mapping.
- **Dockerfile:** `AK.Gateway/AK.Gateway.API/Dockerfile` · **Build context:** repository root.
- **In-cluster:** deployed as `ak-gateway` (ClusterIP on 8080) by the Helm chart, and is the **only** service exposed externally — through the cluster ingress, which terminates TLS in front of it. See the [AKS Guide](../docs/guides/aks-guide.md#ingress-and-tls).
- **Downstream routes:** ak-products, ak-cart, ak-order, ak-payments. Identity is **Microsoft Entra ID** — an external managed service validated at the gateway and each downstream service, not a routed container; the standalone identity service was retired (see [ADR-021](../docs/adr/ADR-021-retire-identity-service-for-entra.md)).

---

## Development Overrides

`ocelot.Development.json` replaces the downstream hosts with localhost dev ports, for running the gateway locally against services started on the host:

```
Products  → http://localhost:5077
Cart      → http://localhost:5079
Orders    → http://localhost:5080
Payments  → http://localhost:5086
```

It is a **full replacement** of `ocelot.json`, not a merge: `Program.cs` loads `ocelot.{Environment}.json` when it exists (Development) and otherwise `ocelot.json` (Production / in-cluster).
