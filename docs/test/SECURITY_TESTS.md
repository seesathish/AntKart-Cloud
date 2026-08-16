# Security Testing

> **Placeholder — this guide is being rebuilt.** The detailed black-box / grey-box security procedures that previously lived here have been cleared. Security verification will be rewritten from scratch against the live cloud platform (`https://api.antkart.in`) as part of the planned security work — the scope has grown well beyond the old per-endpoint checks.

## What is planned

Security testing is tracked on the [Roadmap](../ROADMAP.md) under two items:

- **Test guide set — Security / ethical-hacking guide.** Black-box / grey-box probes run against the live services through the ingress: authentication (401 on unauthenticated and tampered/`alg:none` tokens), authorization (403 for non-admins on admin routes), ownership / IDOR (a user cannot read or mutate another user's order, payment, or cart — identity comes from the JWT `sub`), input and mass-assignment handling, and exposure (no stack traces, no `Server` header, per-route rate limiting).
- **Security programme.** The cross-cutting programme across identity, network, runtime, supply chain, data, detection, and governance — DAST, Kubernetes network policies, pod-security admission, dependency and image scanning, policy enforcement, audit logging with alerting, image signing, customer-managed keys, secret rotation, and threat modelling.

## In the meantime

- The security boundaries the platform **enforces today** are described in [Security — how it is secured](../development/6-security.md).
- Open security defects awaiting this work are tracked in the [Known Issues Register](../KNOWN_ISSUES.md) (notably **KI-002**).
- The automated `dotnet test` unit + integration suites (which cover authorization rules, ownership checks, and input validation at the handler level) remain the layer-agnostic baseline — see the [Testing index](README.md).
