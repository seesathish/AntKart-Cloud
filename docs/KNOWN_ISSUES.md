# Known Issues Register

The authoritative record of known defects and deferred fixes. Each item is acknowledged and has a planned resolution — items here are scheduled, not overlooked. Each has a unique `KI-NNN` id, a **severity** (High / Medium / Low), its **impact**, the **current mitigation**, and the **planned resolution**.

Related: the [Platform Roadmap](ROADMAP.md) tracks delivered/in-progress/planned work; this register tracks defects specifically.

---

## Open

### KI-002 · Discount gRPC accepts unverified tokens · Severity: High

- **Component:** `AK.Discount/AK.Discount.Grpc` — `Interceptors/AuthInterceptor.cs`, `Program.cs`.
- **Impact:** The interceptor calls `JwtSecurityTokenHandler.ReadJwtToken`, which **decodes** the bearer token **without verifying its signature, issuer, audience, or expiry**, then authorizes the admin write RPCs (create/update/delete discount) on a `roles=admin` claim. `Program.cs` registers no authentication middleware. A **forged, unsigned token** containing `roles=admin` would therefore be accepted for those RPCs.
- **Current mitigation:** The service is **`ClusterIP`-only** (no ingress, never exposed outside the cluster) and is reached solely by the Products service over cluster DNS, so the vulnerable surface is not externally reachable. Read-only RPCs (the normal discount lookup) do not depend on this check.
- **Planned resolution:** Replace the decode-only check with **proper token validation** (signature, issuer, audience, lifetime — the same Entra validation the REST services use via `AK.BuildingBlocks`) as part of the security workstream (see the [Roadmap security programme](ROADMAP.md#planned--future)).

### KI-005 · No stock-release compensation on payment failure · Severity: Medium

- **Component:** `AK.Order` saga / payment outcome path — `PaymentFailedConsumer` and the order saga.
- **Impact:** When a payment **fails**, the order is moved to `PaymentFailed`, but the **stock reserved earlier by the saga is not released**. The reservation is retained indefinitely, so inventory stays held for an order that will not be paid — a slow leak of available stock as failed payments accumulate.
- **Current mitigation:** None automated. The reservation can be released manually if needed; failed-payment volume is low in the current usage. Note this is separate from the order **state-machine** fix (Confirmed → Paid / PaymentFailed), which is resolved — this item is only about the **compensation** that should follow a failure.
- **Planned resolution:** A deliberate design decision to defer to a dedicated **compensation workstream** — emit a stock-release/compensation event on `PaymentFailed` (and on cancellation) so the reservation is returned. Not implemented yet; tracked here so it is scheduled, not overlooked.

### KI-003 · Gateway CORS allows any origin · Severity: Medium

- **Component:** `AK.Gateway/AK.Gateway.API/Program.cs` — the `AllowAll` CORS policy (`AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`).
- **Impact:** The gateway accepts cross-origin requests from **any** origin. Acceptable for development, but too permissive for production, where the browser same-origin protections it relaxes should be scoped to the known front-end origins.
- **Current mitigation:** Authentication and authorization are still enforced (Entra JWT at the edge and in each service), so permissive CORS does not by itself grant access; and no production front end is yet served from this edge.
- **Planned resolution:** Replace `AllowAnyOrigin` with an explicit allowed-origins list sourced from configuration, applied when the managed edge (Azure API Management, [ADR-020](adr/ADR-020-api-management-managed-edge-gateway.md)) and a real front-end origin are in place.

### KI-004 · Mutable image tag can serve a stale image · Severity: Low

- **Component:** Container image delivery (all services) — mutable tag (e.g. `dev`) with `imagePullPolicy: IfNotPresent`.
- **Impact:** A node that has already cached a tag keeps serving the **old** image after a new image is pushed to the **same** tag, so a code change appears not to deploy.
- **Current mitigation:** Delete the pod (or roll the Deployment) to force a fresh pull; documented in the [AKS Guide troubleshooting](guides/aks-guide.md#troubleshooting).
- **Planned resolution:** Tag images with the **immutable commit SHA** so every rollout references a distinct image; adopted with the CI/CD delivery pipeline ([ADR-022](adr/ADR-022-cicd-github-actions-oidc.md)).

### KI-007 · Key Vault purge protection blocks early teardown/rebuild · Severity: Low

- **Component:** `infrastructure/environments/dev/key-vault` — the `kv-antkart-dev` vault (`purge_protection_enabled = true`, `soft_delete_retention_days = 7`).
- **Impact:** Purge protection was enabled **out of band** on the live vault and Azure **cannot disable it once on**. With it enabled and a **7-day** soft-delete retention, the vault **cannot be purged early**, and after deletion its **globally-unique name (`kv-antkart-dev`) stays reserved for 7 days**. Any teardown-and-rebuild that recreates the vault under the **same name** inside that window fails — which blocks the planned **zero-to-AKS rebuild runbook** (a clean destroy → re-apply of the dev environment).
- **Current mitigation:** The Terraform config now records reality (`purge_protection_enabled = true`), so routine applies no longer fail trying to disable it. For a rebuild within the retention window, either **wait out the 7 days** before recreating, or recreate the vault under a **different name**. **QA is unaffected:** no Azure Policy enforces purge protection (the governance module only creates a budget), so `kv-antkart-qa` was created with `purge_protection_enabled = false` and is freely disposable.
- **Planned resolution:** Accept purge protection as the standing posture for `kv-antkart-dev` (the safer, production-like default) and update the zero-to-AKS rebuild runbook to account for the name-reservation window (wait it out, or use a fresh vault name). No code change is pending; recorded so the rebuild constraint is scheduled, not overlooked.

### KI-010 · Budget start date is hardcoded and expires · Severity: Medium

- **Component:** `infrastructure/environments/dev/governance/terragrunt.hcl` line 36 — `start_date = "2026-06-01T00:00:00Z"`.
- **Impact:** Azure rejects a monthly consumption budget whose start date precedes the current month with `400: Start date for monthly time grain should not be prior to current month.` The date was correct when dev was first built and has since expired, so **the dev environment can no longer be provisioned from its own Terraform**. The defect is invisible while the budget already exists; it only surfaces on a rebuild. It was found by building the QA environment from the same modules, where the same hardcoded date failed on first apply. This directly undermines the zero-to-AKS rebuild runbook.
- **Current mitigation:** QA was unblocked by setting its own `start_date` to the first day of the current month. Dev's live budget is unaffected and continues to operate; only a rebuild would fail.
- **Planned resolution:** Derive the start date rather than hardcode it — `formatdate` over `timestamp()` to produce the first of the current month, with `lifecycle { ignore_changes = [time_period] }` so a changing timestamp does not produce a perpetual diff. Confirm the module's existing lifecycle rules before changing dev, since without an ignore rule a plan against dev would show a change on the live budget. ADR-worthy: hardcoded dates in infrastructure silently expire.

### KI-011 · Two Key Vault secrets have no consumer · Severity: Low

- **Component:** `kv-antkart-dev` — secrets `cosmos-connection-string` and `servicebus-connection-string`.
- **Impact:** A repository-wide search of the service projects finds no reference to either name. Services reach Service Bus through `ServiceBus:FullyQualifiedNamespace` plus workload identity, and Cosmos through `MongoDbSettings:ConnectionString`. Both are residue from before the secret-less migration. A Service Bus connection string embeds a shared access key, so anyone able to read it can connect as a fully authorised client, bypassing the workload-identity controls the platform is built on. They are dormant credentials, not spare configuration.
- **Current mitigation:** Neither was created in the QA environment — QA seeds eight secrets, not ten. Access to dev's vault is already restricted by RBAC to the workload identities, the Terraform service principal and the administrator.
- **Planned resolution:** Verify no consumer exists **outside** the service projects — Function App application settings, CI/CD workflows, and the seed tools under `AK.Tools` were not covered by the in-code audit — then delete both from dev's vault and confirm the environment still starts. To be done as a standalone task, not during another environment build.

### KI-012 · Provider lock files are stale in the dev environment · Severity: Low

- **Component:** `infrastructure/environments/dev/*/.terraform.lock.hcl`
- **Impact:** `root.hcl` generates a shared `required_providers` block declaring azurerm, azuread and random into every unit. Units whose locks were written before that block existed record only azurerm — the dev `key-vault` unit is an example, holding one provider where the equivalent QA unit holds three. A lock file records what was resolved at the last `init` and is not refreshed when provider requirements change centrally, so azuread and random are effectively unpinned for those units and would resolve to whatever is current on a fresh init.
- **Current mitigation:** azurerm — the provider that governs how every resource is actually created — is pinned at 4.76.0 and matches across environments, so there is no behavioural drift in practice. Only the units that genuinely use azuread (`app-registration`) and random (`postgresql`) exercise those providers.
- **Planned resolution:** Re-run `terragrunt init` across the dev units to refresh their lock files, and commit the result. Low priority; fold into the next piece of work that already touches dev's Terraform. Same class of defect as an unpinned install tag — a value captured once and never revisited.

### KI-013 · Configuration changes do not restart pods · Severity: High

- **Component:** `deploy/helm/antkart-service/templates/deployment.yaml` — the pod template carries no checksum annotation for the ConfigMap it consumes.
- **Impact:** The chart renders `.Values.env` into a ConfigMap and the Deployment consumes it via `envFrom.configMapRef`. Changing a value in Git updates the ConfigMap but leaves the pod template byte-identical, so Kubernetes performs no rollout and running pods keep the values they read at startup. **Every signal reports healthy while the cluster runs stale configuration**: Argo CD shows `Synced`/`Healthy`, `.status.sync.revision` matches Git HEAD exactly, and the ConfigMap holds the new value — only `kubectl exec ... printenv` reveals the drift. This defeats the core GitOps guarantee that the cluster reflects Git. It was found on QA when a seeding flag changed in Git had no effect on running pods, and it applies identically to dev; any configuration change made to dev to date may not have taken effect until an unrelated restart.
- **Current mitigation:** `kubectl rollout restart deploy/<service> -n antkart` after any configuration change. Diagnose by comparing the ConfigMap value against `printenv` inside the pod. Documented as a callout in Phase 5 of the provisioning runbook.
- **Planned resolution:** Add a checksum annotation to the pod template so it changes whenever the ConfigMap does: `checksum/config: {{ include (print $.Template.BasePath "/configmap-env.yaml") . | sha256sum }}`. Applies to the shared chart, so it fixes every environment at once. Verify by changing a value in Git and confirming Argo rolls the pods without manual intervention.

### KI-014 · MassTransit lacks management-plane rights on Service Bus · Severity: High

- **Component:** `infrastructure/environments/*/role-assignments` — the service identities are granted Azure Service Bus Data Sender and Data Receiver only.
- **Impact:** MassTransit reconciles its own topology at startup, calling `CreateOrUpdateSubscription`. That is a management-plane operation; Sender and Receiver are data-plane roles and do not permit it, so the bus faults with `401 SubCode=40100 Unauthorized` even though Terraform has already created the topic and subscriptions. **The failure is logged as a warning, not an exception** — pods remain `Running`, Kubernetes reports them `Healthy`, and Argo reports `Synced`, while asynchronous messaging is silently broken. Only the pod logs reveal it. Found on QA at first startup; dev appears unaffected only because its subscriptions were established out of band — dev's namespace carries a subscription named with MassTransit's own convention that QA's does not, indicating MassTransit successfully created topology there under credentials not captured in code.
- **Current mitigation:** QA's four bus-using identities (products, cart, order, payments) were granted Azure Service Bus Data Owner at namespace scope by hand, outside Terraform. The grant is not in code and will not survive a rebuild.
- **Planned resolution:** Decide between two approaches and record the choice as an ADR. (a) Grant Data Owner in the `role-assignments` unit and let MassTransit manage its own topology, accepting that a compromised service could reshape the messaging topology. (b) Keep least-privilege Sender/Receiver and pre-create every subscription, rule and property in Terraform, accepting that topology changes then require an infrastructure change. Whichever is chosen, the grant must move into code — the current state is a manual step that a rebuild would lose.

---

## Resolved

### KI-009 · Order state machine rejected `Confirmed → Paid` / `Confirmed → PaymentFailed` · Resolved

- **Component:** `AK.Order/AK.Order.Domain` — `Order.cs`, the `_allowedTransitions` map.
- **Cause:** The order status state machine had **no edge** from `Confirmed` to `Paid` or to `PaymentFailed`. The saga moves an order `Pending → Confirmed` on a successful stock reservation, then drives it to `Paid` / `PaymentFailed` on the payment outcome — but `UpdateStatus()` threw `InvalidOperationException` for those two transitions, so the payment result could not be applied and the order stalled at `Confirmed`.
- **Proof (not theoretical):** surfaced during the **full cloud saga end-to-end run** (the "AntKart Cloud E2E Saga" Postman flow), where an order that paid successfully never advanced past `Confirmed`.
- **Fix:** added `Confirmed → Paid` and `Confirmed → PaymentFailed` to `_allowedTransitions` in `AK.Order.Domain/Order.cs` (**PR #8**), shipped through the CI/CD → GitOps pipeline. Verified by re-running the saga E2E to a `Paid` terminal state, and the failure branch to `PaymentFailed`. (Stock-release compensation on the failure path is a separate, still-open concern — see [KI-005](#ki-005--no-stock-release-compensation-on-payment-failure--severity-medium).)
- **Lesson:** an explicit state machine must cover **every** transition the orchestrator actually drives; a full end-to-end saga run is what exposes a missing edge that unit tests on individual handlers do not.

### KI-006 · CD path filters lacked a markdown exclusion · Resolved

- **Component:** `.github/workflows/*-cd.yml` (all six) and `*-ci.yml` — the `on: push`/`pull_request` **path filters**.
- **Cause:** each workflow filtered on whole service folders (`AK.<Service>/**`) and the shared library (`AK.BuildingBlocks/**`) with **no exclusion for documentation**. Any file under those folders — including `*.md` — matched, so a documentation-only change triggered a full CD run (build image → push to ACR → bump `.image.tag` in Helm values). Because `AK.BuildingBlocks/**` is present in **all six** workflows, a single change to `AK.BuildingBlocks/BUILDING_BLOCKS.md` matched every service.
- **Proof (not theoretical):** commit **`5261027`** changed **only markdown** (diagram docs + Phase-1 SUPERSEDED banners, touching `AK.*/…​.md` and `AK.BuildingBlocks/BUILDING_BLOCKS.md`). It triggered **five** CD pipelines, which each built and pushed an image and bumped the tag — commits **`c2ad36e`** (shoppingcart), **`0f97859`** (gateway), **`7b5280d`** (order), **`1be49cd`** (discount), **`5685cf2`** (payments), all `-> 5261027`.
- **Fix:** append **`- '!**/*.md'`** as the **last** entry of every workflow's `paths` list (later patterns take precedence in GitHub Actions path filters, so the negation must come last). Documentation changes inside a service folder no longer trigger CD or the CI gate. The five already-pushed tag bumps were **left in place** — the images are functionally identical to what preceded them, and reverting would trigger another round of syncs.
- **Lesson:** a positive path filter over a folder is over-broad by default; pair it with a negative pattern (`!**/*.md`, or narrower globs) so non-shipping files cannot drive delivery.

| ID | Component | Resolution |
|----|-----------|------------|
| KI-001 | AK.Discount.Grpc | The gRPC `AuthInterceptor` read a nested, provider-specific role-claim structure, so the admin write RPCs would fail authorization once tokens carried roles in a flat `roles` claim. **Resolved in the identity migration to Microsoft Entra ID:** the interceptor now reads the flat top-level `roles` claim, consistent with the shared `AK.BuildingBlocks` authentication. (Note: correct role *reading* was restored; cryptographic token *validation* remains open — see KI-002.) |
| — | AK.Gateway | **Ocelot downstream host drift resolved.** `ocelot.json` used Docker-era hostnames (`ak-*-api`) while the deployed Helm ConfigMap used the Kubernetes Service names (`ak-*`); `ocelot.json` was reconciled to the `ak-*` Service names so source and deployed config agree. |
| — | AK.Gateway | **Development-profile startup failure resolved.** `Program.cs` loaded `ocelot.json` and `ocelot.Development.json` via `AddJsonFile`, which merged their `Routes` arrays by index and produced a duplicate upstream route so Ocelot refused to start. The gateway now loads exactly one ocelot file selected by environment; verified to start and route correctly in both Development and Production. |
| — | Documentation | **Broken anchor links resolved.** `DevelopmentGuide.md` linked `README.md#architecture-overview`, which did not resolve (the heading is "Architecture"); corrected, and a repo-wide anchor-link sweep fixed any others. |

## Withdrawn

### KI-008 · AK.Discount.Grpc `/metrics` unreachable by a standard Prometheus scrape · Withdrawn

- **Status:** Withdrawn — **no longer applicable.** The self-hosted Prometheus/Grafana metrics stack, and all metrics collection, were **removed** from the platform ([ADR-025 — Superseded decision](adr/ADR-025-observability-architecture.md#superseded-decision--self-hosted-metrics-removed)). There is no scrape and no `/metrics` endpoint on any service, and `AK.Discount.Grpc` reverted to a single HTTP/2-only Kestrel endpoint — so the reachability problem this item tracked cannot occur.
- **History:** the issue tracked that Discount's HTTP/2-only (h2c) gRPC port could not serve an HTTP/1.1 Prometheus scrape. It was resolved with a dedicated HTTP/1.1 metrics listener (PR #18) and wired into Prometheus (PR #19); both — along with the entire metrics stack — have since been removed. Recorded here rather than deleted so the change history stays visible.

## Notes

- Resolved items are recorded here (and in the change history) once verified. **Withdrawn** items are ones that became moot before/after resolution because the feature they concerned was removed.
- New deferred issues are added under **Open** with a unique `KI-NNN` id, a severity, the affected component, the impact, the current mitigation, and a planned resolution.
