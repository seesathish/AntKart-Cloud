# ADR-006: Ocelot API Gateway over YARP

## Status
Accepted

## Context
The platform needs a single entry point for all microservices that handles routing, JWT authentication, per-route rate limiting, and circuit breaking (QoS). Without a gateway, clients would need to know the addresses of all downstream services, token validation would only happen per-service with no edge-level rejection, and cross-cutting concerns (rate limits, correlation IDs) would be duplicated in every service.

Two candidates were evaluated:
- **Ocelot** — mature .NET gateway, JSON-based route config, built-in rate limiting and QoS, large community
- **YARP** (Yet Another Reverse Proxy) — Microsoft-maintained, higher throughput, more flexible, but lower-level

## Decision
Use Ocelot 23.4.2. The JSON route config (`ocelot.json`) is human-readable and maps directly to Ocelot's concepts (upstream path, downstream host, rate limit options). Rate limiting and circuit breaker settings are per-route without writing code. Development overrides (`ocelot.Development.json`) allow different downstream addresses when running services locally.

## Consequences
**Easier:** Non-developers can read and modify the route config. Adding a new service requires only a new JSON block. Per-route rate limits (10–30 RPS) and circuit breaker settings are declarative.

**Harder:** Route config changes require a service restart (no hot-reload in Ocelot). At extreme scale (>50k RPS), YARP's lower overhead would be preferable. Ocelot does not support streaming responses well — a future file-download or Server-Sent Events endpoint would need to bypass the gateway.
