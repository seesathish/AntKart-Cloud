# ADR-025 — Observability Architecture: Serilog for Logs, OpenTelemetry for Traces and Metrics

**Status:** Accepted — **metrics portion superseded** (see [Superseded decision](#superseded-decision--self-hosted-metrics-removed))
**Date:** 2026-08-02
**Area:** Observability
**Relates to:** ADR-013 (Key Vault RBAC and observability foundation — the Log Analytics workspace and Application Insights resource), ADR-018 (AKS and workload identity — the cluster whose OMS agent collects container logs), ADR-019 (serverless notification Functions — the one component still on the classic Application Insights SDK)

---

> **Update (2026-08-04) — the metrics half of this decision was SUPERSEDED.** Decision point 3 (OpenTelemetry metrics exposed at `/metrics`), and the self-hosted Prometheus/Grafana stack later built on it (`kube-prometheus-stack` via Argo CD, per-service ServiceMonitors, the AK.Discount HTTP/1.1 metrics port), were **implemented and then deliberately removed** — see [Superseded decision — self-hosted metrics removed](#superseded-decision--self-hosted-metrics-removed) below. The **logs and traces** decisions are unchanged and remain in force. The original text is left intact as the historical record.

---

## Context

The platform *documented* an observability story long before it had a working one, and the documentation drifted from reality in both directions.

- **Correlation was documented but did not work.** Serilog was configured to enrich every log with `ServiceName`, `Environment`, and `CorrelationId`, but the console sink used the **default text formatter**, which renders the message template and **discards the enriched structured properties**. The properties existed in the pipeline and never reached the collector, so filtering logs by `CorrelationId` — the whole point — returned nothing useful.
- **The correlation middleware was wired in one service, not six.** `CorrelationIdMiddleware` was registered in a single service's `Program.cs`; the other five never emitted or propagated `X-Correlation-Id`, so even a working formatter would not have produced a cross-service trail.
- **No telemetry left AKS.** The six cluster services exported **no traces and no metrics**. There was no distributed-trace view at all; a slow request that fanned out across Order → Payments → Products could not be followed. Only `func-antkart-notifications-dev` reported to Application Insights, via the classic SDK it was scaffolded with.
- **The docs claimed Application Insights collected the service logs.** In fact nothing shipped the AKS logs anywhere until the cluster's Azure Monitor Container Insights (OMS) agent was collecting stdout — and it lands them in **Log Analytics**, not Application Insights.

The goal of this decision is a **secret-less, low-friction, demonstrable** observability stack for a six-service platform on AKS plus one Functions app — logs, traces, and metrics that actually resolve against the live workspace, with logs and traces joinable.

---

## Decision

**Split the three signals by the tool that carries each best, and make correlation real.**

1. **Logs — Serilog, structured JSON to the console, collected by Azure Monitor.**
   Every service and Function writes logs with Serilog using the **`RenderedCompactJsonFormatter`**, so each line is a JSON object that **preserves** the enriched properties (`ServiceName`, `Environment`, `CorrelationId`, and now `TraceId`/`SpanId`). The AKS **Container Insights (OMS) agent** ships container stdout into the **Log Analytics** workspace `log-antkart-dev`, landing in the legacy **`ContainerLog`** table (the cluster is on the pre-DCR collection path — `useAADAuth=false`, no data collection rules — so not `ContainerLogV2`). Queries `parse_json(LogEntry)` to read the structured fields. No code-side sink credentials.
   `CorrelationIdMiddleware` is now wired in **all** services, and an `ActivityEnricher` stamps `TraceId`/`SpanId` onto every log line.

2. **Traces — OpenTelemetry, exported to Application Insights via the Azure Monitor exporter.**
   All six services register the OpenTelemetry SDK and export spans through the **Azure Monitor Trace Exporter**; spans land in **`AppRequests`** (server) and **`AppDependencies`** (client). Instrumented: **AspNetCore, HttpClient, gRPC client, MassTransit (Service Bus), Npgsql (PostgreSQL), MongoDB (Cosmos Mongo API), and StackExchange.Redis**. W3C `traceparent` propagates across HttpClient / gRPC / MassTransit, so a request is followable across services.

3. **Metrics — OpenTelemetry, exposed in Prometheus exposition format at `/metrics`.**
   Each service serves `/metrics` for a future Prometheus scrape. The exposition endpoint is delivered; **the scrape and dashboards are not** (see *Known limitations* and the Roadmap).

### Why OTel log export is deliberately NOT enabled

The OpenTelemetry SDK can also export **logs** to Application Insights (`AppTraces`). We deliberately **do not** enable it for the six services:

- **Serilog already covers logs**, structured and correlated, in `ContainerLog`. Turning on OTel log export would emit **the same log events a second time** into `AppTraces`, **doubling log ingest cost** for no new information.
- **Logs and traces are already joinable** without duplication: every Serilog line carries `TraceId`/`SpanId`, so a slow span in `AppRequests` pivots directly to its log lines in `ContainerLog`, and vice-versa. The link is by id, not by shipping logs twice.

`AppTraces` therefore contains only `func-antkart-notifications-dev` (the Functions app's classic SDK), which is expected.

### OpenTelemetry version pin (1.13.x)

The OpenTelemetry packages are pinned to the **1.13.x** line (on `Microsoft.Extensions.*` 9.0.0), **not** bumped to 1.14.0+. This is deliberate: **OpenTelemetry 1.14.0 and later depend on `Microsoft.Extensions.*` 10.x**, which on a `net9.0` target trips **NU1605 package-downgrade errors** against the 9.0.x framework references. The pin is recorded here so that a Dependabot bump to 1.14.0+ is understood as a **known-breaking** change to be held until the platform targets .NET 10 — not silently merged, and not reverted without knowing why it was pinned.

---

## Considered Alternatives

### Ship logs through OpenTelemetry too (one pipeline for all three signals) — rejected

A single OTel pipeline for logs, traces, and metrics is architecturally tidy. Rejected because it would **duplicate** the log stream Serilog already delivers to `ContainerLog` — doubling ingest cost — while adding nothing: the `TraceId`/`SpanId` enrichment already links Serilog logs to OTel traces. Tidiness does not justify paying twice to ingest the same log events.

### Azure Managed Grafana for dashboards — rejected

Azure Managed Grafana would give hosted dashboards over the metrics with little setup. Rejected: it carries a **standing monthly cost** for a portfolio platform that is frequently torn down and rebuilt, it is **Azure-locked**, and it is **less demonstrable** than the intended path — deploying the **Prometheus operator and Grafana into the cluster via GitOps**, which shows the Kubernetes-native, portable observability stack the platform is meant to exhibit. Dashboards are deferred to that self-hosted path (Roadmap), not bought as a managed add-on.

### Trace sampling now — rejected for now

Head- or tail-based sampling would cap trace-ingest cost. Rejected **for now**: at the platform's current volume the traces are cheap, and sampling should be tuned against **measured real ingest**, not guessed at before there is any. Revisit once the workspace shows what ingest actually is.

---

## Consequences

**Positive**

- **Correlation actually works.** Structured JSON preserves the enriched fields, the middleware runs in every service, and a single `CorrelationId` (or `TraceId`) now stitches a request across all six services in `ContainerLog`.
- **Distributed tracing where there was none.** A fan-out request is followable end-to-end in Application Insights across HTTP, gRPC, Service Bus, PostgreSQL, Cosmos, and Redis spans.
- **Secret-less and low-cost.** Logs ride the cluster's OMS agent; traces use the Azure Monitor exporter under workload identity; no sink credentials in code, no duplicated log ingest.
- **Logs join traces by id.** `TraceId`/`SpanId` on every log line makes the span ↔ log pivot work without shipping logs twice.

**Trade-offs / Known limitations**

- **Metrics are exposed but not scraped.** `/metrics` is live on every service, but Prometheus and Grafana are **not deployed** — there are no metric dashboards yet. Tracked on the Roadmap.
- **`AK.Discount.Grpc` `/metrics` is unreachable by a standard scrape.** Its single Kestrel listener is HTTP/2-only (cleartext `h2c`) to serve gRPC, and a Prometheus scrape speaks HTTP/1.1. The correct fix is a **second HTTP/1.1 listener** dedicated to `/metrics`; switching the gRPC port to `Http1AndHttp2` is not acceptable (no TLS/ALPN over `h2c`, so gRPC breaks). See [KI-008](../KNOWN_ISSUES.md).
- **The Notification Functions app is still on the classic Application Insights SDK**, not OpenTelemetry — so it reports to `AppTraces`/`AppRequests` through the classic path rather than the OTel exporter. Aligning it onto OTel is deferred; recorded so the split is understood, not mistaken for a gap.

---

## Superseded decision — self-hosted metrics removed

**Date:** 2026-08-04 · **Supersedes:** Decision point 3 (metrics) above, and the self-hosted metrics stack built on it.

The metrics half of this ADR was carried well past "exposed but not scraped": a full **self-hosted kube-prometheus-stack** was delivered — Prometheus + Grafana deployed via Argo CD under a scoped `monitoring` AppProject, a per-service `ServiceMonitor` scraping each `/metrics` endpoint (with cross-namespace discovery), the AKS control-plane scrape targets disabled, and a dedicated **HTTP/1.1 metrics listener on AK.Discount.Grpc** (which resolved the h2c-scrape problem noted under *Known limitations* above). It worked end to end.

It was then **deliberately removed**, for two reasons:

- **Operational complexity disproportionate to a two-node dev cluster.** Prometheus + Grafana — with their CRDs, RBAC, cross-namespace discovery, first-install sync ordering, and sizing on a tight node pool — is a standing maintenance cost out of proportion to the value delivered on a small dev cluster.
- **A deliberate choice not to present depth that has not been earned.** A shallow, half-maintained metrics stack is worse than none.

**What was removed:** the `deploy/argocd/monitoring/` Application and the `monitoring` AppProject; the chart's `servicemonitor.yaml` template, the `serviceMonitor`/`metricsPort` values, and the optional metrics port on the Deployment/Service; the OpenTelemetry **metrics pipeline** in `AK.BuildingBlocks` (`AddPrometheusExporter`, the runtime-metrics instrumentation, `MapObservabilityEndpoints`, and the Prometheus exporter package); and AK.Discount's second HTTP/1.1 Kestrel listener (back to a single HTTP/2-only gRPC endpoint).

**What is unchanged:** everything about **logs and traces** — Serilog → Log Analytics, OpenTelemetry **tracing** and the Azure Monitor trace exporter, the `ActivityEnricher`, and the OTel 1.13.x version pin — plus the reasoning in *Why OTel log export is deliberately NOT enabled*. `KI-008` (the Discount h2c-scrape limitation) is now **moot** and marked Withdrawn.

**Replacement direction:** a **managed observability platform — Datadog, under evaluation** — for metrics (and potentially a single pane over logs and traces too). Tracked on the [Roadmap](../ROADMAP.md). Note that the *Azure Managed Grafana* alternative above was rejected in favour of the self-hosted path; with that path now withdrawn, the managed-platform question is reopened on different terms — a full product evaluation, not a dashboards add-on.

The original decision above is left intact as the historical record: this section supersedes its metrics portion, it does not rewrite it.

---

## Notes

How observability actually works today — with the **working** KQL against `log-antkart-dev` — is described in [Observability — how it is seen](../development/5-observability.md). The earlier Phase-1 intent (superseded, retained for history) is in [observability concepts](../guides/observability-concepts.md). The Log Analytics workspace and Application Insights resource this ADR builds on were provisioned in [ADR-013](ADR-013-key-vault-rbac-and-observability-foundation.md).
