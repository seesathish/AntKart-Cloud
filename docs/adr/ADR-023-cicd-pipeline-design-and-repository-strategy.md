# ADR-023 — CI/CD Pipeline Design and Repository Strategy

**Status:** Accepted
**Date:** 2026-07-27
**Area:** DevOps & DevSecOps
**Relates to:** ADR-022 (CI/CD on GitHub Actions with OIDC federated credentials to Azure), ADR-018 (AKS workload identity), ADR-020 (managed edge gateway)

---

## Context

ADR-022 chose GitHub Actions with OIDC federation as the CI/CD platform and settled how it authenticates to Azure. It deliberately left the **shape** of the pipelines open. This ADR records that shape: how the repository is organised for delivery, how many pipelines there are and what triggers them, how a build reaches the cluster, and which tests run where.

The constraints that drive the design:

- **Monorepo, many services.** All six services and the shared library live in one repository. A change usually touches one service. Rebuilding and redeploying all six on every commit would be slow, noisy, and would couple independent services.
- **The cluster is already Git-driven.** Argo CD reconciles the cluster to the state in Git (ADR-020 area, see the [GitOps Guide](../guides/gitops-guide.md)). Delivery must fit that model rather than push to the cluster out of band.
- **Secret-less posture.** No component holds long-lived cloud or cluster credentials (ADR-022). The delivery design must not reintroduce cluster credentials into CI.
- **Polyrepo-ready.** The services are self-contained by design; a service should be extractable into its own repository later with minimal change. The pipeline layout must not assume a monorepo forever.

We establish the pattern with **one service (Products)** and template it to the others once proven.

## Decision

**Per-service, path-filtered pipelines; two decoupled workflows per service (CI on pull request, CD on merge); delivery by updating an image tag in Git for Argo CD to reconcile; a shared library Helm chart reused per service via values; tests run as unit + in-memory integration tiers in CI.**

### 1. Shared library Helm chart, reused per service

One generic chart, `deploy/helm/antkart-service`, is instantiated per service through a per-service values file (`deploy/helm/values/<service>.yaml`). Each service is independently deployable (its own Argo CD Application, its own image tag) but shares one chart definition.

### 2. Per-service, path-filtered pipelines

Each service has its own workflow files, triggered only when paths that affect that service change — the service folder, the shared `AK.BuildingBlocks`, and (for CI) the tests that cover it. A change to one service does not build or deploy another.

### 3. Pattern B — two workflows per service, decoupled through Git

- A **CI workflow** runs on `pull_request`. It is the **quality gate**: build, test (with coverage), static analysis (SonarCloud), and security scanning (Trivy). It builds and pushes **no image** and touches **no cluster**.
- A **CD workflow** runs on **merge to `master`**. It builds an **immutable image**, pushes it to the registry, and **updates the image tag in Git**. Argo CD reconciles that change onto the cluster.

The two are **coupled only through Git** — the merge that CI guarded is the same commit CD acts on. There is no direct hand-off between the workflow runs.

### 4. GitOps delivery — CD updates Git, Argo CD deploys

CD's final act is a commit that sets the service's image tag to the freshly built, immutable tag (the commit SHA). Argo CD detects the change and performs the rolling update. **CI/CD never runs `helm` or `kubectl` against the cluster and never holds cluster credentials** — the only actor that talks to the cluster is Argo CD, from inside it. CD needs only registry-push rights (via OIDC) and permission to commit to the repository.

### 5. Test tiers in CI

CI runs two tiers that need **no live infrastructure**: **unit tests** (per service, e.g. `AK.Products.Tests`) and **in-memory integration tests** (`AK.IntegrationTests`, using the MassTransit in-memory harness — no broker, no database, no running host). Live end-to-end verification against the running cluster is a **separate, post-deploy concern**, not a PR gate.

### 6. Self-contained-service / polyrepo-ready

Workflows, values, and tests are organised so a service could be lifted into its own repository with minimal change: per-service workflow files, per-service values, path filters rooted at the service folder, and a chart that is referenced rather than embedded per service.

## Considered Alternatives

### Chart strategy

- **Per-service charts (chosen against).** A full Helm chart inside each service folder. Maximum independence and the most natural fit if every service moved to its own repository — but six near-identical charts to keep in step, and a cross-cutting change (probe strategy, security context, labels) becomes six edits. Rejected for this team topology, where one small team owns all services and consistency matters more than per-service chart divergence. Recorded as an **evolution path** for when services diverge or move to separate repositories.
- **Umbrella (parent) chart (rejected).** One chart with all six services as subcharts/dependencies, deployed as a unit. It re-couples the services into a single release — the opposite of independent deployability — and makes a one-service change a whole-platform deploy. Rejected outright.
- **Shared library chart, reused per service (chosen).** One chart definition, six independent instantiations via values. DRY for cross-cutting concerns, yet each service keeps its own release and image tag. Best fit for one team owning consistent services that must still deploy independently.

### Workflow structure

- **A single workflow doing CI and CD (rejected).** One workflow that tests on PR and also deploys. It blurs the quality gate and the delivery act, tends to accumulate `if` conditions on event type, and risks a deploy path that runs in a PR context. Rejected for muddling two concerns with different triggers, permissions, and failure semantics.
- **`workflow_run` chaining — CD triggered by CI completion (rejected).** A CD workflow that starts when the CI workflow finishes. It creates a brittle, implicit dependency (CD keys off another workflow's conclusion and must re-establish which commit it refers to), and `workflow_run` runs in the default-branch context with its own permission and re-entrancy subtleties. Rejected in favour of decoupling through Git.
- **Pattern B — CI on PR, CD on merge, decoupled through Git (chosen).** Each workflow has one clear trigger, one clear purpose, and its own least-privilege permissions. The merge commit is the natural, auditable boundary between "guarded" and "delivered". No workflow depends on another workflow's run.

### Delivery mechanism

- **CI/CD pushes to the cluster with `kubectl`/`helm` (rejected).** The pipeline would hold cluster credentials and mutate the cluster directly — reintroducing the exact credential custody ADR-022 removes, and bypassing the Git source of truth that Argo CD reconciles. Rejected.
- **CD updates the image tag in Git; Argo CD deploys (chosen).** Delivery is a Git commit; the cluster is changed only by Argo CD from inside it. CI/CD holds no cluster credentials, and every deployment is an auditable, revertible commit.

## Consequences

**Positive**

- **Fast, isolated feedback** — a one-service change builds and deploys only that service.
- **A clear quality gate** — CI on PR must pass before merge; CD acts only on merged, guarded commits.
- **No cluster credentials in CI/CD** — the only actor touching the cluster is Argo CD; CD needs registry-push and repo-commit rights only. Deployments are auditable Git commits, revertible with a `git revert`.
- **Consistency with independent deployability** — one chart to maintain, six independent releases.
- **Polyrepo-ready** — the per-service layout ports to separate repositories with minimal change.

**Trade-offs**

- **CD commits to the repository.** The pipeline writes an image-tag commit to Git. It is scoped to the values file and clearly attributed; a protected `master` must allow that specific automated update (or the update targets a separate location — see evolution path).
- **A same-repo config update can retrigger workflows.** The tag-update commit must not create a loop; it is confined to paths that do not trigger CI/CD (or uses `[skip ci]` semantics / a bot identity), which the CD workflow will encode explicitly.
- **One Sonar project for the repository.** Analysis is keyed to the repository; per-service pipelines contribute their own coverage. If per-service analysis granularity is later required, this becomes per-service Sonar projects.

## Evolution path (future options)

- **Separate configuration repository.** Move the deployment manifests/values (what CD updates and Argo CD watches) into their own repo, so application CI/CD and delivery state are fully decoupled and the app repo need not receive automated commits.
- **Per-service Helm charts.** If services diverge or move to their own repositories, promote the shared library chart into a per-service chart (or a versioned shared chart dependency).
- **Per-service Sonar projects** and **release/environment promotion** (dev → staging → prod) layered on the same Pattern B foundation.

## Notes

The concrete workflow files and the end-to-end journey (PR → checks → merge → image build/push → Git tag update → Argo CD sync → rolling update) are documented in the [DevOps CI/CD Guide](../guides/devops-cicd-guide.md). This ADR records the design; ADR-022 records the platform and authentication choice it builds on.

---

## Amendment (2026-07-27) — As-built, verified end to end

The design above was implemented for **Products** and **proven end to end**. This amendment records the as-built specifics that were left open or decided differently at build time. The original decisions stand; the points below refine the two trade-offs the design flagged as "to be encoded" (the automated-commit mechanism and loop prevention) and add the identity and branch-protection specifics.

**Delivery identity — OIDC, AcrPush only, no cluster access.** CD authenticates to Azure via a user-assigned managed identity **`id-ak-cicd-dev`** (Terraform: `infrastructure/modules/github-oidc` + the dev unit) with GitHub **federated credentials** — issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, subjects `repo:seesathish/AntKart-Src3:ref:refs/heads/master` and `:environment:dev`. Its **only** role is **AcrPush on the registry** — it has **no cluster access at all**, which is the concrete realisation of "no cluster credentials in CI/CD": the pipeline literally cannot deploy or touch the cluster even if it tried. The identity's `client_id` / `tenant_id` / `subscription_id` are **GitHub repository variables** (identifiers, not secrets).

**Immutable images.** Each build is tagged with the **commit SHA** (short form), never a mutable tag — a given tag always means exactly one build. (Verified: a `/version` change deployed as image tag `ede8b04`.)

**The automated tag-bump push — a scoped PAT.** The original trade-off noted the tag-bump commit "must … use `[skip ci]` semantics / a bot identity". As-built, the `github-actions` bot is **not a listable bypass actor** in this repository's ruleset UI, so CD instead pushes with a **fine-grained Personal Access Token** (repo secret `CD_PUSH_TOKEN`, **Contents: read/write on this repository only**) owned by the admin account, which **is** on the branch-ruleset bypass list. The **auto-merge-PR alternative was rejected on a concrete deadlock**: a values-only PR never matches the path-filtered required check (`products-ci` is scoped to `AK.Products/**` etc.), so that required check sits **"Expected" forever** and the PR can never merge. The [separate configuration repository](#evolution-path-future-options) evolution removes the need for this token entirely.

**Loop-prevention correction.** The original trade-off assumed a bot/`GITHUB_TOKEN` identity, whose pushes do not trigger workflows. Because the tag-bump uses a **PAT**, and **PAT pushes *can* trigger workflows**, the real guard is the **path filter** — the tag-bump writes only `deploy/helm/values/**`, which is outside CD's trigger paths — with `[skip ci]` as belt-and-suspenders. The token is not the loop guard.

**Branch-protection split — application code vs. infrastructure/docs.** The ruleset **`master-protection`** (Active) requires a PR plus **four** status checks (`build-test`, `sonar`, `trivy`, and `SonarCloud Code Analysis`) before merge, with **Repository admin** on the bypass list. This is a deliberate split: **application code** flows through PR + the CI gate; **infrastructure and documentation** may be pushed directly by an admin and are gated **separately** (a `terraform plan` review today; an infrastructure pipeline later). Application changes are never un-gated; infrastructure is gated by its own mechanism rather than the application test suite.

**Delivery is auto-synced.** Argo CD **auto-sync** (`selfHeal: true`, `prune: false`) is enabled on the `ak-products` Application, so the tag-bump commit deploys with **no manual sync** — the full loop (code change → PR → gate → merge → build → ACR → Git tag bump → Argo CD auto-sync → running pod) runs hands-free.

The remaining five services still run their manually-installed images; templating the two workflows (and enabling auto-sync) to them is the next step, and is intentionally out of scope for this first, proven pattern.

---

## Amendment (2026-07-28) — Templated to all six services

The two workflows were templated from Products to the remaining five services (**ShoppingCart, Order, Payments, Discount, Gateway**) — each a verbatim copy of the Products pair (same SHA-pinned actions, same `9.0.x` SDK, same SonarCloud org/project, same Trivy gate, same OIDC auth, same `CD_PUSH_TOKEN` tag-bump, same loop prevention) with only per-service specifics changed:

- **ShoppingCart** publishes to **`antkart/shoppingcart`**, not `antkart/cart` (`cart.yaml` sets `image.name: shoppingcart`); CD tags/pushes that repo and bumps `.image.tag` in `cart.yaml`.
- **Discount is gRPC** — its deployable is `AK.Discount.Grpc` and CD builds from that Dockerfile; it is not referenced by `AK.IntegrationTests`, so CI runs unit tests only.
- **Gateway has no test project** — its `build-test` job builds only and `sonar` runs without coverage (job names kept as `build-test`/`sonar`/`trivy` so the same four required checks are satisfied); CD bumps only `.image.tag`, leaving the gateway's ingress values untouched.
- **Integration tests** run in CI only for the services `AK.IntegrationTests` references — Products, ShoppingCart, Order, Payments.

Argo CD auto-sync is enabled per service as each adopts the loop (Products first). The single repository-level SonarCloud project is unchanged — every service analyses into it, scoped by what its run builds (the "one Sonar project" trade-off above).
