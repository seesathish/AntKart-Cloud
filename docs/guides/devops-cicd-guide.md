# DevOps CI/CD Guide

This guide explains how a code change becomes a running pod on the cluster, and how the pipelines are built. It is written for a reader learning CI/CD — every concept is explained rather than assumed. The design decisions behind it are recorded in [ADR-023](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) (pipeline design and repository strategy) and [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md) (GitHub Actions + OIDC to Azure).

The pattern was established with **one service — Products — first** and **proven end to end**: a `/version` change flowed through the full loop — PR → gate → merge → image build → ACR → Git image-tag bump → Argo CD auto-sync — and was served from a pod running the exact commit-SHA image tag, hands-free. It is now **templated to all six services** — each has a `<service>-ci.yml` (pull-request quality gate) and a `<service>-cd.yml` (merge delivery), all copies of the Products pair with only the per-service specifics changed (see the [per-service table](#per-service-specifics)).

---

## What CI/CD means here

- **CI — Continuous Integration.** Every proposed change is automatically built and checked before it can merge. The goal is that `master` is always in a known-good state: it compiles, its tests pass, and it clears the quality and security bars. CI produces a **verdict**, not a deployment.
- **CD — Continuous Delivery.** Once a change is merged, it is automatically turned into a deployable artifact (a container image) and delivered to the cluster. Here, "delivered" means **written into Git** for Argo CD to apply — not pushed directly.

The two are **separate workflows** with different triggers, purposes, and permissions (this is *Pattern B* in ADR-023). CI guards the door; CD walks through it after the merge.

---

## The end-to-end journey — from code change to running pod

```
 developer                GitHub                          Azure / cluster
 ─────────                ──────                          ───────────────
 1. push branch,   ──►   2. CI workflow runs on the PR
    open PR                 build → test(+coverage)
                           → SonarCloud → Trivy
                              │
                              ▼  all checks green (required)
 3. review + merge  ──►   4. CD workflow runs on master
    to master               build immutable image (tag = commit SHA)
                           → push image to ACR  (auth via OIDC, no secret)
                           → update image tag in Git (a commit)
                              │
                              ▼
                         5. Argo CD (in-cluster) sees Git changed
                           → syncs → rolling update  ──►  6. new pods live
```

1. **A developer opens a pull request.** The change lives on a branch; the PR is the proposal to merge it into `master`.
2. **CI runs as the quality gate.** The Products CI workflow builds the service, runs the unit and in-memory integration tests with coverage, runs SonarCloud static analysis, and runs Trivy security scanning. These appear as **status checks** on the PR.
3. **Review and merge.** A human reviews; branch protection requires the CI checks to be green before the merge button is enabled. Merging produces one commit on `master`.
4. **CD runs on the merge.** The CD workflow builds an **immutable image** tagged with the commit SHA, pushes it to the Azure Container Registry (authenticating with an **OIDC token exchanged for an Entra token — no stored secret**), and then **updates the service's image tag in Git** (a commit to the values the cluster watches).
5. **Argo CD reconciles.** Argo CD runs *inside* the cluster, watches Git, notices the tag changed, and — with **auto-sync enabled** on `ak-products` — performs a rolling update to the new image with no manual step. See the [GitOps Guide](gitops-guide.md).
6. **New pods are live.** The Deployment rolls the ReplicaSet to the new image; the old pods drain once the new ones are Ready.

No step 4–6 command runs against the cluster from GitHub. The only actor that changes the cluster is Argo CD, from within it.

---

## Why CD is GitOps, not a push deploy

A traditional pipeline *pushes*: it holds cluster credentials and runs `kubectl`/`helm` from the CI runner into the cluster. This platform does the opposite — CD's final act is a **Git commit that changes the desired image tag**, and the in-cluster Argo CD *pulls* that change and applies it. The benefits:

- **CI/CD holds no cluster credentials.** The runner never authenticates to the Kubernetes API. It needs only rights to push an image (via OIDC) and to commit to the repository. This keeps the platform's secret-less posture intact (ADR-022).
- **Every deployment is an auditable, revertible commit.** What is running is exactly what Git says; rolling back is a `git revert`, not a scramble to remember the last-known-good image.
- **One source of truth.** The same Git state drives Argo CD whether a human or a pipeline wrote it — no out-of-band cluster mutations to reconcile.

CD *builds and publishes*; Argo CD *deploys*. The handoff between them is Git.

---

## The per-service workflow layout

Workflows are **per service and path-filtered**, so a change to one service does not build the others. Each service has two workflow files (Pattern B):

| Workflow | Trigger | Purpose | Touches the cluster? |
|----------|---------|---------|----------------------|
| `<service>-ci.yml` | `pull_request` affecting that service | Quality gate: build, test, SonarCloud, Trivy | No |
| `<service>-cd.yml` | push to `master` affecting that service | Build + push image, update image tag in Git | No — writes Git; Argo CD deploys |

#### Per-service specifics

All six services run the identical two-workflow pattern; only these facts differ per service. Everything else (the SHA-pinned action versions, `DOTNET_VERSION: '9.0.x'`, the SonarCloud org/project, the `HIGH,CRITICAL` Trivy gate, the OIDC auth, `CD_PUSH_TOKEN`, `[skip ci]` + path-filter loop prevention) is copied verbatim from Products.

| Service | Deployable project | Image repo | Values file | Unit tests | Integration tests in CI | Ingress |
|---------|--------------------|-----------|-------------|-----------|------------------------|---------|
| Products | `AK.Products.API` | `antkart/products` | `products.yaml` | ✅ `AK.Products.Tests` | ✅ | — |
| ShoppingCart (`cart`) | `AK.ShoppingCart.API` | **`antkart/shoppingcart`** | `cart.yaml` | ✅ `AK.ShoppingCart.Tests` | ✅ | — |
| Order | `AK.Order.API` | `antkart/order` | `order.yaml` | ✅ `AK.Order.Tests` | ✅ | — |
| Payments | `AK.Payments.API` | `antkart/payments` | `payments.yaml` | ✅ `AK.Payments.Tests` | ✅ | — |
| Discount | **`AK.Discount.Grpc`** (gRPC) | `antkart/discount` | `discount.yaml` | ✅ `AK.Discount.Tests` | — (not referenced) | — |
| Gateway | `AK.Gateway.API` | `antkart/gateway` | `gateway.yaml` | — (no test project) | — (not referenced) | ✅ (tag-bump untouched) |

Three services need special handling, all reflected above:

- **ShoppingCart → `antkart/shoppingcart`.** The image repository is **not** `antkart/cart`. `cart.yaml` sets `image.name: shoppingcart`, so `cart-cd.yml` builds, pushes, and tags `antkart/shoppingcart` and bumps `.image.tag` in `cart.yaml`.
- **Discount is gRPC.** Its deployable is `AK.Discount.Grpc` (no `.API` project); CI builds that project and CD builds from `AK.Discount/AK.Discount.Grpc/Dockerfile`. It is **not** referenced by `AK.IntegrationTests`, so its CI runs unit tests only (no integration-test step, and `AK.IntegrationTests/**` is not in its path filter).
- **Gateway has no test project and an ingress.** `AK.Gateway` is a thin Ocelot routing host with no unit suite, so `gateway-ci.yml`'s `build-test` job **builds only** and its `sonar` job runs **without a coverage report** (the job names stay `build-test`/`sonar`/`trivy` so the same four required checks are satisfied). `gateway-cd.yml` bumps **only** `.image.tag` in `gateway.yaml` — the gateway's ingress values (and the Argo CD Application's ingress parameters) are never touched, so a tag bump is safe.

**Integration tests belong to the services `AK.IntegrationTests` actually references** — Products, ShoppingCart, Order, Payments. Those four include `AK.IntegrationTests/**` in their CI path filter and run it; Discount and Gateway do not (that project does not reference them).

The Products CI workflow is [`.github/workflows/products-ci.yml`](../../.github/workflows/products-ci.yml). Its `pull_request` trigger is **path-filtered** to the paths that can affect Products:

- `AK.Products/**` — the service itself
- `AK.BuildingBlocks/**` — the shared library every service compiles against
- `AK.IntegrationTests/**` — the in-memory integration tests that exercise Products (and related) flows
- `nuget.config` — package-restore behaviour affects the build
- the workflow file itself — so a change to the gate is validated by the gate

The workflow is structured as **three jobs**, so a reviewer sees the gate stages distinctly:

1. **`build-test`** — restores, builds the deployable API in Release, and runs both test tiers with coverage. This is the fast compile-and-test gate; it fails early on a broken build or a failing test.
2. **`sonar`** — runs SonarCloud analysis (see below). It `needs: build-test`, so analysis is not spent on a commit that does not even compile.
3. **`trivy`** — security scanning (see below). Independent of the build, so it runs in parallel as its own stage.

### The two test tiers in CI

CI runs the tiers that need **no live infrastructure**, so the gate is fast and deterministic:

- **Unit tests** — `AK.Products.Tests` (pure, mocked dependencies).
- **In-memory integration tests** — `AK.IntegrationTests`, using the **MassTransit in-memory harness**: the event-driven flows are exercised with no broker, no database, and no running host.

Live end-to-end verification against the running cluster is a **separate, post-deploy concern** (see [Full-cloud end-to-end](../test/1-full-cloud-end-to-end.md)), not a PR gate — it needs a deployed environment and would make the gate slow and flaky.

### .NET version and coverage format

- **.NET SDK 9.0.x.** Detected from the repository, not guessed: there is no `global.json`, and every project sets `<TargetFramework>net9.0</TargetFramework>`. The workflow pins `DOTNET_VERSION: '9.0.x'`.
- **Coverage in OpenCover format.** The test projects already reference `coverlet.collector`; the workflow collects coverage with `--collect:"XPlat Code Coverage;Format=opencover"`. **OpenCover** is chosen because SonarCloud consumes it natively for .NET via `sonar.cs.opencover.reportsPaths` — no format conversion step.

---

## How SonarCloud fits as a PR gate

**SonarCloud** is a hosted static-analysis service: it inspects the code for bugs, code smells, security hotspots, duplication, and — wired correctly — **test coverage**, and reports back on the pull request.

For .NET, analysis uses the **SonarScanner for .NET** in a three-step wrapper around the build:

```
dotnet sonarscanner begin /k:<projectKey> /o:<org> /d:sonar.token=$SONAR_TOKEN \
       /d:sonar.host.url=https://sonarcloud.io \
       /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"
dotnet build   … (Release)
dotnet test    … (produces the OpenCover coverage reports)
dotnet sonarscanner end /d:sonar.token=$SONAR_TOKEN
```

- **`begin`** starts analysis and registers where to find coverage (`sonar.cs.opencover.reportsPaths`). The scanner hooks into MSBuild, so it must wrap a real **build** — it cannot analyse a prebuilt output. That is why the `sonar` job runs its own build rather than reusing `build-test`'s.
- **`build` + `test`** compile the code the scanner observes and produce the OpenCover reports.
- **`end`** uploads the analysis and coverage to SonarCloud, which posts the result on the PR.

Configuration in the workflow: organization **`seesathish`**, project key **`seesathish_AntKart-Src3`** (the repository-level SonarCloud project; a single project key per repo — per-service Sonar projects are a future option in ADR-023), and the coverage path wired to the OpenCover reports so **SonarCloud shows real coverage**, not zero.

**All six services analyse into this same SonarCloud project**, each CI run scanning the projects it builds. This is the intended replication — the same org/project key across every `<service>-ci.yml`, scoped by what each run compiles — not a per-service project. (Gateway has no tests, so its `sonar` run omits the coverage report and analyses code only.) The single-project trade-off is recorded in ADR-023; moving to per-service Sonar projects is the documented evolution if per-service granularity is later needed.

- **`SONAR_TOKEN` is a GitHub Actions secret** — a SonarCloud token, not a cloud credential. It already exists in the repository secrets. It is the only secret CI needs.
- **Fork PRs.** GitHub does not pass secrets to workflows triggered by PRs from forks, so the `sonar` job cannot authenticate on a fork PR. For an internal, same-repo branch flow this does not arise; handling fork contributions (e.g. a `pull_request_target` variant) is a later refinement.

---

## How Trivy fits as a PR gate

**Trivy** is an open-source security scanner. In this gate it runs two scans and **fails the job** on anything at or above a configurable severity — the single knob `TRIVY_SEVERITY`, set to **`HIGH,CRITICAL`** (so LOW/MEDIUM findings are reported by tooling but do not block a merge):

1. **Filesystem / dependency scan** of the Products tree (`scan-type: fs`, scanners `vuln,secret,misconfig`) — vulnerable dependencies, accidentally-committed secrets, and IaC/Dockerfile misconfiguration.
2. **Dockerfile scan** (`scan-type: config`) of `AK.Products/AK.Products.API/Dockerfile` — image-build misconfiguration (e.g. running as root, missing pinned bases).

Both use `exit-code: 1`, so a finding at the threshold fails the check and blocks the merge. `ignore-unfixed` is a commented switch to optionally suppress vulnerabilities that have no fix available yet.

> **Action supply chain (ADR-022).** Every third-party Action in the Products CI workflow (including Trivy) is **pinned to an immutable commit SHA** — a version tag can be repointed, a commit SHA cannot — with the release version kept as a trailing `# vX.Y.Z` comment for readability. When bumping an Action, look up the SHA its new release tag points to and update both the SHA and the comment.

---

## Branch protection — making CI a required gate

Branch protection is what turns "the checks ran" into "the checks *must pass* before merge". As-built, `master` is protected by the ruleset **`master-protection`** (Active) in **Settings → Rules → Rulesets**:

1. **Target:** the `master` branch.
2. **Require a pull request before merging** — no direct pushes to `master` for gated content; changes arrive via PR. (Optionally require at least one approving review.)
3. **Require status checks to pass before merging** — the **four** required checks (they appear once each workflow has run once so GitHub knows their names):
   - `build-test` — the compile + test job
   - `sonar` — the SonarScanner-for-.NET job in the workflow
   - `trivy` — the security-scan job
   - `SonarCloud Code Analysis` — SonarCloud's own quality-gate status posted back on the PR
4. **Require branches to be up to date before merging** — the PR must include the latest `master` so the checks reflect the post-merge state.
5. **Block force-pushes and deletions** of `master`.
6. **Bypass list:** **Repository admin**. This is what lets the CD tag-bump push (and admin infrastructure/doc pushes) proceed — see below.

With this in place, an application-code PR cannot merge until all four checks are green — CI is a genuine gate, not just a report.

### The deliberate split — application code vs. infrastructure/docs

The bypass list (Repository admin) is a **deliberate design**, not a loophole:

- **Application code** (`AK.*/**`) always flows through a **PR + the CI gate** — it is never pushed directly, because that is the code the tests and scanners exist to guard.
- **Infrastructure and documentation** (Terraform, guides, ADRs, workflow files) may be pushed **directly by an admin** and are gated **separately** — a reviewed `terraform plan` today, and an infrastructure pipeline later. Running the application test suite over a Terraform or Markdown change would gate the wrong thing.

So both classes are gated; they are gated by the mechanism that fits each. The admin bypass exists to serve that split (and to let the CD tag-bump through — next section), not to wave code past the gate.

### Secrets and OIDC

- **`SONAR_TOKEN`** — a GitHub Actions repository secret (SonarCloud token). It **must exist** for the `sonar` job to authenticate; it already does. This is the only secret CI uses.
- **Azure authentication uses OIDC — no stored secret.** The CD workflow authenticates to Azure (to push the image) by exchanging a short-lived **GitHub OIDC token** for an Entra token via a **federated credential**, scoped to this repository and the `refs/heads/master` ref. The `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` it uses are repository **variables** — identifiers, not secrets — so nothing long-lived is stored in GitHub (ADR-022). CI, being a pure quality gate, needs no Azure access at all.

### Workflow-level vs job-level permissions

The `GITHUB_TOKEN` that every workflow run receives is scoped by a `permissions:` block. Where that block sits matters:

- **Workflow level** — the grant applies to **every job** in the workflow, whether or not a job needs it.
- **Job level** — the grant applies to **only that job**. This is least privilege: each job gets exactly the token scopes its steps use, and nothing else.

**Prefer job level for any `write` scope.** A write permission declared at workflow level is over-broad — it hands, say, `contents: write` to jobs that only read. SonarCloud flags this ("Move this write permission from workflow level to job level"), and it is correct.

The CD workflows are the worked example. Each has two jobs with different needs, so permissions are declared **per job**:

```yaml
# no workflow-level permissions block
jobs:
  build-and-push:
    permissions:
      id-token: write   # mint the OIDC token azure/login exchanges (secret-less Azure auth)
      contents: read    # checkout only
  update-gitops:
    permissions:
      contents: read    # the git push uses the CD_PUSH_TOKEN PAT, not GITHUB_TOKEN
```

Note `update-gitops` needs only `contents: read`, not `write`: its checkout and `git push` authenticate with the **`CD_PUSH_TOKEN`** PAT (see [Branch protection and the CD tag-bump](#branch-protection-and-the-cd-tag-bump)), so the `GITHUB_TOKEN` never performs the write — there is no `contents: write` anywhere in CD. The CI workflows keep a single workflow-level `permissions: contents: read`: it is **read-only** (not a write, so not flagged) and is genuinely needed by all three jobs (each checks out), so workflow level is the correct shared scope there.

---

## CD — delivery on merge to master

The CD workflow, [`.github/workflows/products-cd.yml`](../../.github/workflows/products-cd.yml), triggers on **push to `master`** affecting `AK.Products/**` or `AK.BuildingBlocks/**` (not `pull_request` — that is CI's job). It has two jobs:

**Job 1 — `build-and-push`:**

1. **Compute an immutable tag** — the short commit SHA (`${GITHUB_SHA::7}`). A given tag always means exactly one build (the opposite of a mutable `dev`/`latest` tag).
2. **Authenticate to Azure with OIDC — no secret.** `azure/login` exchanges a short-lived GitHub OIDC token for an Entra token for the `id-ak-cicd-dev` identity, using three repository **variables** — `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (identifiers, not secrets; provisioned by the `github-oidc` Terraform unit). The presented OIDC subject `repo:seesathish/AntKart-Src3:ref:refs/heads/master` matches the identity's federated credential. The job declares `permissions: id-token: write` — required to mint the OIDC token.
3. **Build and push.** `az acr login` wires the OIDC session into Docker; the image is built from `AK.Products/AK.Products.API/Dockerfile` with the **repository root as build context** (matching the manual build — the Dockerfile COPYs repo-root-relative paths), tagged with the SHA (and `latest`), and pushed to `acrantkartdev.azurecr.io/antkart/products:<sha>`. The identity's only privilege is **AcrPush**.

**Job 2 — `update-gitops`** (this is the deploy):

4. **Bump the image tag in Git.** It sets **`.image.tag`** in `deploy/helm/values/products.yaml` to the new SHA with `yq -i '.image.tag = strenv(TAG)'`. That field does not exist in the file today (the chart default is `dev`); `yq` **creates** the `image.tag` key — which is why `yq` is used rather than `sed` (sed cannot add a missing key). The value is passed via the environment (`strenv`) so it can never be interpreted as an expression.
5. **Commit and push (rebase-and-retry).** It commits `chore(cd): products image -> <sha> [skip ci]` and pushes to `master`. Because a change to `AK.BuildingBlocks/**` trips all six services' path filters, all six CD workflows run and push to `master` at once; the first pushes fast-forward and the rest are rejected as non-fast-forward. The push is therefore wrapped in a **bounded rebase-and-retry loop** (up to five attempts): on a rejection it runs `git pull --rebase origin master` and retries — always conflict-free, since each service edits a **different** values file — and **fails loudly** if it still cannot land after five tries (the checkout uses `fetch-depth: 0` so the rebase has real history to work with). A final **verification step** then fetches `master` and asserts the tag that was built actually landed, so a lost race can never masquerade as success. The push is authenticated by the scoped **`CD_PUSH_TOKEN`** PAT (the commit author is `github-actions[bot]`, cosmetic) — see the branch-protection section below for why, and [ADR-024](../adr/ADR-024-cd-gitops-write-contention.md) for the write-contention race this prevents.
6. **Argo CD deploys.** Argo CD watches `deploy/helm/values/products.yaml`; the commit makes the `ak-products` Application `OutOfSync`, and — with **auto-sync enabled** (`selfHeal: true`, `prune: false`) — it rolls the Deployment to the new image **with no manual sync** (see the [GitOps Guide](gitops-guide.md)). **CD runs no `helm`/`kubectl` and holds no cluster credentials** — its "deploy" action is the Git commit.

**Loop prevention.** The tag-bump commit only touches `deploy/helm/values/**`, which is **not** in the CD path filter, so it can never retrigger CD — this is the real guard. Belt-and-suspenders: CI is `pull_request`-only (a push never matches it) and the message carries `[skip ci]`. Note the tag-bump push uses a **PAT**, and — unlike the built-in `GITHUB_TOKEN` — a PAT push *can* trigger workflows, so the path filter, not the token, is what prevents a loop.

### Branch protection and the CD tag-bump

`master` requires a pull request and passing status checks, so the CD job's **direct push** of the tag-bump would be blocked by default. Three options were considered:

- **(a) Let the automated tag-bump bypass branch protection** — the deterministic CD commit to the values file pushes directly; humans still go through PRs.
- **(b) Have CD open a PR that auto-merges** once checks pass.
- **(c) Move the deploy manifests to a separate path/branch (or a config repo)** outside this protection.

**Chosen: (a).** For a solo maintainer it is the simplest and is secure in this shape: the tag-bump is a machine-generated, single-line change to a values file, and the **application code it points to already passed the CI gate** on its way into `master`, so the bump adds no unreviewed application code. Option (b) is fragile here — the required check `products-ci` is path-filtered to `AK.Products/**` etc. and would **never run** on a values-only PR, so the required-check would sit forever "Expected", deadlocking auto-merge; and a PR opened with `GITHUB_TOKEN` does not trigger workflows anyway (it would need a PAT or GitHub App). Option (c) is cleaner long-term and is recorded as the evolution path in [ADR-023](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) (separate config repository), and **removes the need for the token below entirely** — but adds moving parts now.

**How the bypass is implemented — a scoped PAT (`CD_PUSH_TOKEN`).** The natural way to do (a) would be to add the **`github-actions` bot** to the `master` ruleset's bypass list — but this repo's ruleset UI does **not** expose the Actions bot as a listable bypass actor. Instead, Job 2 authenticates its push with a **fine-grained Personal Access Token** stored as the repository secret **`CD_PUSH_TOKEN`**:

- The token belongs to the **repo-admin account**, which **is** on the `master` ruleset's bypass list — so a push authenticated as that account satisfies branch protection. `actions/checkout` in Job 2 is given `token: ${{ secrets.CD_PUSH_TOKEN }}`, which persists it as the git credential, so the subsequent `git push` is authenticated as the admin (not the default `GITHUB_TOKEN` / `github-actions[bot]`).
- **Minimal scope:** a fine-grained PAT limited to **this repository only** (`AntKart-Src3`) with a single permission — **Contents: Read and write**. Nothing else (no Actions, no admin, no other repos).
- **Expiration and rotation:** fine-grained PATs expire; set a finite expiry (e.g. 90 days) and **rotate before it lapses** — an expired token makes Job 2's push fail with an auth error (the image still builds and pushes; only the tag-bump commit fails). Re-issue with the same scope and update the `CD_PUSH_TOKEN` secret.
- **Note the loop-prevention consequence:** because a PAT push (unlike a `GITHUB_TOKEN` push) *can* trigger workflows, the loop is prevented by the **path filter** (the tag-bump touches `deploy/helm/values/**`, which is not in CD's trigger) plus CI being `pull_request`-only, plus `[skip ci]` — not by the token. See the CD workflow header.

**One-time setup:** create the fine-grained PAT (Contents: read/write on `AntKart-Src3`) on the admin account and save it as the repository secret `CD_PUSH_TOKEN` (**Settings → Secrets and variables → Actions → New repository secret**). The token is the one CD-side secret; the Azure auth remains secret-less via OIDC.

---

## The CD identity — OIDC to Azure, AcrPush only

CD's Azure access is a **user-assigned managed identity**, `id-ak-cicd-dev`, provisioned as Terraform in [`infrastructure/modules/github-oidc`](../../infrastructure/modules/github-oidc) and its dev unit (`infrastructure/environments/dev/github-oidc`). It mirrors the workload-identity model the cluster pods use — federation, not a stored secret.

**How the token exchange works.** The identity carries **GitHub federated credentials**: they trust GitHub's OIDC issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, for two exact-match subjects:

- `repo:seesathish/AntKart-Src3:ref:refs/heads/master` — a run on the `master` branch (what CD uses).
- `repo:seesathish/AntKart-Src3:environment:dev` — a run targeting the GitHub Environment `dev` (available if a job declares `environment: dev`).

At run time, `azure/login` presents the workflow's short-lived GitHub OIDC token; Entra checks the issuer/audience/subject against a federated credential and, on an exact match, returns an Entra access token. **No client secret exists** — there is nothing to store or rotate on the Azure side.

**Least privilege — AcrPush, and nothing else.** The identity's only role is **AcrPush on the ACR** (`acrantkartdev`). It has **no cluster access whatsoever** — it cannot run `kubectl`/`helm`, read cluster secrets, or deploy. This is the security payoff of the GitOps split: even a fully-compromised CD run can only push an image; it cannot change what runs, because that is Argo CD's job, driven from Git.

**The three GitHub variables.** `azure/login` needs the identity's `client_id`, its `tenant_id`, and the `subscription_id`. These are **identifiers, not secrets** (they name an identity; they do not authenticate as it — the OIDC token does), so they are stored as repository **variables** `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID`, read in the workflow as `${{ vars.* }}`. Get their values from the applied unit: `terragrunt output client_id` (and `tenant_id` / `subscription_id`).

---

## Argo CD auto-sync — closing the loop

For the loop to be hands-free, Argo CD must apply the tag-bump without a human clicking **Sync**. **Auto-sync is enabled** on the `ak-products` Application:

```bash
kubectl -n argocd patch application ak-products --type merge \
  -p '{"spec":{"syncPolicy":{"automated":{"selfHeal":true,"prune":false}}}}'
```

- **`automated`** — Argo CD syncs automatically whenever Git changes (so the CD tag-bump deploys on its own).
- **`selfHeal: true`** — if the live cluster drifts from Git, Argo reverts it back to Git.
- **`prune: false`** — Argo does **not** delete resources that disappear from Git (a conservative default; enable pruning deliberately once you trust the manifests). See the [GitOps Guide](gitops-guide.md) for the staged enablement rationale.

For a lasting change, set the same `syncPolicy` in the `ak-products` Application manifest and commit it, rather than only patching the live object.

---

## One-time human setup (checklist)

These are the manual, one-time steps a human performs in the GitHub / SonarCloud / Azure UIs — everything else is code. All secret/token values are placeholders; never commit them.

1. **SonarCloud project + token.** Create the project (organization `seesathish`, project key `seesathish_AntKart-Src3`) in SonarCloud, generate a token, and save it as the repository **secret** `SONAR_TOKEN` (Settings → Secrets and variables → Actions → **Secrets**). This is the only secret CI uses.
2. **Azure OIDC identity variables.** Apply the `github-oidc` Terraform unit, then read its outputs and save three repository **variables** (Settings → Secrets and variables → Actions → **Variables**): `AZURE_CLIENT_ID` = `terragrunt output -raw client_id`, `AZURE_TENANT_ID` = `… tenant_id`, `AZURE_SUBSCRIPTION_ID` = `… subscription_id`. Variables, not secrets — they are identifiers.
3. **CD push token.** Create a **fine-grained PAT** on the admin account, scoped to **`AntKart-Src3` only** with **Contents: Read and write** (nothing else), and save it as the repository **secret** `CD_PUSH_TOKEN`. Set a finite expiry and rotate before it lapses.
4. **Branch ruleset.** Create the `master-protection` ruleset (Active): require a PR + the four status checks, block force-push/deletion, and put **Repository admin** on the bypass list (see [Branch protection](#branch-protection--making-ci-a-required-gate)).
5. **Argo CD auto-sync.** Enable auto-sync on `ak-products` (the patch above), or commit the `syncPolicy` into its Application manifest.

---

## Troubleshooting

- **A tag-bump PR sits on "Expected" forever (required-check deadlock).** If you ever route the tag-bump through a PR instead of a direct push, the required check `products-ci` is path-filtered to `AK.Products/**` etc. and **never runs** on a values-only change — so GitHub shows the check as "Expected" indefinitely and the PR cannot merge. This is exactly why CD pushes directly with `CD_PUSH_TOKEN` rather than opening a PR (and why option (b) was rejected in ADR-023).
- **CD's image builds and pushes, but the tag-bump step fails with a Git auth error.** The `CD_PUSH_TOKEN` PAT has **expired** (fine-grained PATs are time-bounded). Only the `update-gitops` job fails — the image is already in ACR. Re-issue the PAT with the same scope (Contents: read/write on this repo) and update the `CD_PUSH_TOKEN` secret; re-run the job.
- **`kubectl port-forward` to Argo CD fails on Windows (IPv6 quirk).** On Windows, `kubectl port-forward svc/argocd-server 8080:443` may bind `[::1]` (IPv6) so `https://localhost:8080` refuses the connection while `https://127.0.0.1:8080` works. Force IPv4 with `kubectl port-forward --address 127.0.0.1 svc/argocd-server 8080:443`, or browse `https://127.0.0.1:8080` explicitly.

---

## See also

- [ADR-023 — CI/CD pipeline design and repository strategy](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) — the decisions behind this guide.
- [ADR-022 — CI/CD on GitHub Actions with OIDC](../adr/ADR-022-cicd-github-actions-oidc.md) — the platform and authentication choice.
- [GitOps Guide](gitops-guide.md) — how Argo CD turns a Git change into a rolling update.
- [Operations Command Reference](operations-command-reference.md) — the `az`/`kubectl`/`helm`/`argocd` commands with flags explained.
- [`.github/workflows/products-ci.yml`](../../.github/workflows/products-ci.yml) — the Products CI (quality-gate) workflow.
- [`.github/workflows/products-cd.yml`](../../.github/workflows/products-cd.yml) — the Products CD (delivery) workflow.
- [`infrastructure/modules/github-oidc`](../../infrastructure/modules/github-oidc) — the Terraform for the CD federated identity (AcrPush, no secret).
