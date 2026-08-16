# ADR-020 — API Management as the Managed Edge Gateway

**Status:** Accepted  
**Date:** 2026-06-02  
**Relates to:** ADR-006 (Ocelot API Gateway), ADR-017 (Entra ID JWT validation), ADR-018 (AKS cluster and ingress)

---

## Context

External consumers — a web/mobile front end today, and potentially partner integrations later — need a single, stable entry point to the platform. That edge must handle cross-cutting concerns that do not belong inside any individual service: TLS termination, JWT validation, rate limiting and quotas, consumer identification, request/response shaping, and a published, discoverable API surface.

The current in-process design uses Ocelot as the gateway (see ADR-006). Ocelot runs as a service inside the platform: it routes to downstream services, passes JWTs through, and applies per-route rate limiting and QoS. That is a sound choice for an in-cluster, code-owned gateway, and it remains the gateway for the current design.

Once the platform runs on a managed cluster with an internal ingress (see ADR-018), a question arises about the **edge** specifically: should edge concerns continue to be served by an in-process gateway that the team builds and operates, or by a managed edge product, with the cluster's own ingress handling internal routing behind it? This ADR records the **target-state** edge decision and how it relates to the existing Ocelot gateway.

---

## Options Considered

### Option A — In-process gateway (Ocelot) as the external edge

Continue exposing Ocelot directly as the public edge, with downstream services behind it.

**Pros:**
- Already implemented and understood; no new managed product or cost.
- Full control in code; routing and policies live in the repository and version with it.
- No external dependency for the request path.

**Cons:**
- The team builds and operates edge concerns that a managed product provides out of the box: subscription keys/products, quotas, a developer portal, request/response transformation, and analytics.
- The gateway is a stateful, always-on component the team must scale, patch, and keep highly available at the edge — the most exposed point of the system.
- No first-class consumer onboarding (API products, keys, tiered quotas) or self-service documentation portal.
- Edge security (JWT validation, throttling) is coupled to in-process code rather than enforced by a hardened, managed front door.

### Option B — Azure API Management as a managed edge, internal ingress behind it

Azure API Management (APIM) as the public, managed edge; the cluster's internal ingress routes to services behind it. This is a **two-gateway** model: a managed edge for external concerns, plus internal routing inside the cluster.

**Pros:**
- Managed edge: TLS, JWT validation, rate limiting/quotas, subscription keys and **products**, a **developer portal**, request/response **transformation**, caching, and analytics — configured as policy rather than built.
- Offloads edge security and throttling to a hardened managed front door; bad traffic is rejected before it reaches the cluster.
- **VNet integration** keeps backends private — services are reachable only from APIM and the internal ingress, not from the public internet.
- First-class consumer onboarding: products, tiered quotas, and self-service docs without custom code.
- Clear separation of concerns: edge policy at APIM, internal routing at the ingress.

**Cons:**
- A managed service with its own cost and tier model to understand and budget for.
- Two routing layers to reason about (edge + internal ingress) rather than one.
- Policy is expressed in APIM's policy model, a different surface from in-process C# routing.
- Provisioning and configuration (especially VNet integration) add infrastructure complexity.

### Option C — Ingress-only, no managed edge

Expose the cluster's ingress controller directly to the internet with no managed edge in front.

**Pros:**
- Simplest topology; one routing layer.
- No managed-edge cost.

**Cons:**
- Edge concerns (subscription keys, products, quotas, developer portal, transformation, edge JWT validation) are either unavailable or must be reassembled from ingress add-ons and custom code.
- The ingress and the services behind it are directly exposed; the most sensitive boundary has the least managed protection.
- No built-in consumer management or analytics at the edge.

---

## Decision

Adopt **Azure API Management (Developer tier, VNet-integrated)** as the managed external edge for the target state, with the cluster's **internal ingress handling routing** to services behind it. This is a deliberate **two-gateway model**:

- **Managed edge (APIM):** the public front door. It owns edge concerns — TLS termination, **JWT validation** against the platform identity provider (see ADR-017), **rate limiting and quotas**, **subscription keys / products** for consumer identification and tiering, the **developer portal** for discovery and onboarding, and request/response **transformation**. Backends are placed in the VNet so they are not publicly reachable except through APIM.
- **Internal routing (cluster ingress):** behind APIM, the cluster's ingress routes validated, shaped requests to the appropriate service. Internal routing concerns stay inside the cluster (see ADR-018).

### Relationship to the existing Ocelot gateway (ADR-006)

APIM is the **managed-edge evolution** of the gateway role, not a contradiction of ADR-006. The reasoning:

- **Edge concerns move out of an in-process gateway.** TLS, consumer identity, quotas, and a published API surface are operational/edge responsibilities better served by a managed front door than by code the team must run and scale at the most exposed point of the system.
- **Ocelot serves the current in-process design.** It remains the gateway for the present architecture and for purely in-cluster routing scenarios. This ADR frames APIM as the **target-state** edge; it does not require ripping Ocelot out on day one.
- **Two layers, distinct jobs.** In the target state, the edge (APIM) and internal routing (ingress) are separate layers with separate responsibilities, rather than a single in-process component doing both edge and routing.

### JWT validation at the edge vs in-service — defence in depth

Validating the JWT at APIM does **not** remove validation inside the services. Each service continues to validate the token independently (see ADR-017). The two layers are complementary:

- **At the edge (APIM):** reject unauthenticated or malformed requests before they consume cluster resources; enforce coarse, edge-level policy.
- **In the service:** never trust the network; enforce audience, issuer, roles, and ownership/IDOR checks at the point where the action actually happens.

A service must remain secure even if something reaches it without passing through the edge. Edge validation is an optimization and an outer wall — not a replacement for in-service authorization.

### Tier choice — Developer now, Premium when warranted

| Concern | Developer tier | Premium tier |
|---|---|---|
| Intended use | Non-production, evaluation, and pre-production edge | Production at scale |
| SLA | No production SLA | Production SLA |
| VNet integration | Supported | Supported, with multi-region |
| Scale-out / multi-region | Single unit | Multiple units, multi-region deployment |
| Cost | Low | Substantially higher |

Developer tier is selected for the target-state design because it provides the full policy surface (JWT validation, rate limiting, products, developer portal, transformation, VNet integration) needed to prove and operate the edge, at low cost. **Premium is warranted when** production traffic needs an SLA, horizontal scale beyond a single unit, or multi-region resilience — at which point the same policies move up a tier without redesign.

---

## Consequences

### Positive

- **Edge concerns are managed, not built.** Subscription keys/products, quotas, developer portal, transformation, and analytics come from the platform instead of custom code the team must maintain.
- **Smaller, hardened attack surface.** Backends are private (VNet-integrated); only APIM and the internal ingress can reach them, and unauthenticated traffic is rejected at the front door.
- **First-class consumer management.** Products and tiered quotas enable onboarding external/partner consumers with self-service documentation — without writing gateway code.
- **Defence in depth.** Edge JWT validation plus unchanged in-service authorization means no single point's failure exposes the services.
- **Clean separation of layers.** Edge policy (APIM) and internal routing (ingress) have distinct, independently evolvable responsibilities.
- **Tier headroom.** The same configuration scales from Developer to Premium when production SLAs and multi-region are needed.

### Negative / Trade-offs

- **Added cost and a managed dependency.** APIM is a paid service on the request path; its tier and capacity must be budgeted and operated.
- **Two routing layers.** Reasoning about a request now spans the managed edge and the internal ingress, rather than a single component.
- **A second policy surface.** Edge policy is authored in APIM's policy model, distinct from in-process routing config; the team maintains both edge policy and in-service authorization.
- **Developer-tier limits.** No production SLA and single-unit scale mean the Developer tier is unsuitable for production load; moving to Premium is a planned, budgeted step rather than a free switch.
- **Coexistence during transition.** While the platform runs both the existing in-process Ocelot gateway and the target-state APIM edge, responsibilities must be drawn clearly to avoid duplicated or conflicting edge policy.
