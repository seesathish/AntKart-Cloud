# GitHub Actions workflows

Twelve workflows — a **CI** and a **CD** pipeline for each of the six services. They are the
**reference implementation** of the platform's delivery pipeline (see the
[DevOps section](../../docs/development/4-devops.md) and
[ADR-023](../../docs/adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) /
[ADR-022](../../docs/adr/ADR-022-cicd-github-actions-oidc.md)).

> **They will not run as-is in this public copy.** They were built for the original private
> repository and reference secrets, variables, an OIDC federated credential, a SonarCloud
> project, and an Azure Container Registry that do not exist here. On a push or pull request
> in this repo they would fail. Nothing is deleted or disabled — they are kept for reference.

| Workflow | Trigger | What it does |
|---|---|---|
| `<service>-ci.yml` (× 6) | `pull_request` → `master`, path-filtered to the service + `AK.BuildingBlocks/**` (never `**/*.md`) | Quality gate only — no image, no cluster: **build-test** (compile + unit + in-memory integration tests with coverage) → **sonar** (SonarCloud) → **trivy** (filesystem + Dockerfile scan). |
| `<service>-cd.yml` (× 6) | `push` → `master`, path-filtered to the service + `AK.BuildingBlocks/**` (never `**/*.md`) | Delivery: **build-and-push** an immutable commit-SHA-tagged image to ACR via OIDC, then **update-gitops** — bump `.image.tag` in the service's Helm values and push to `master` for Argo CD to reconcile. No `helm`/`kubectl`. |

The six services are `products`, `cart`, `order`, `payments`, `discount`, `gateway`. Each pair is a
copy of the `products` pair with only per-service specifics changed (path filters, the image repo —
e.g. `cart` builds `antkart/shoppingcart`, and `discount` is gRPC).

## What must be configured to run

These are configured on the **original private repository and its Azure subscription**, not here.

### CI (`*-ci.yml`)
| Dependency | Kind | Notes |
|---|---|---|
| `SONAR_TOKEN` | repository **secret** | SonarCloud auth. Not available to fork PRs, so the `sonar` job can't run on forks. |
| `SONAR_ORG` = `seesathish`, `SONAR_PROJECT_KEY` = `seesathish_AntKart-Src3` | workflow `env` | **Hard-wired to the original repo's SonarCloud project** — the project key still names `AntKart-Src3`. |

### CD (`*-cd.yml`)
| Dependency | Kind | Notes |
|---|---|---|
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | repository **variables** (not secrets) | Identify the `id-ak-cicd-dev` user-assigned managed identity for `azure/login`. Plain identifiers, no credential. |
| OIDC **federated credential** | on the Azure identity | Trusts `token.actions.githubusercontent.com` for the subject **`repo:seesathish/AntKart-Src3:ref:refs/heads/master`** (and `:environment:dev`). Because it names the **original repository path**, `azure/login` from this repo (`AntKart-Cloud`) would be rejected. The CD job requests `permissions: id-token: write`. |
| Azure Container Registry `acrantkartdev` (`acrantkartdev.azurecr.io`) | Azure resource | The CD identity needs the **AcrPush** role on it (`az acr login` + `docker push`). |
| `CD_PUSH_TOKEN` | repository **secret** | A fine-grained PAT (Contents: read/write) used to push the `update-gitops` tag-bump to `master`. It must belong to an account on the `master` branch-protection **ruleset bypass list**, or the direct push is rejected. |
| Branch protection ruleset `master-protection` | repository setting | Requires PR + four checks (`build-test`, `sonar`, `trivy`, and SonarCloud's own gate); the CD tag-bump succeeds only because `CD_PUSH_TOKEN`'s account is a bypass actor. |

There is **no** `repository_dispatch` or `workflow_run` trigger and **no** cross-repository action —
CI and CD are coupled only through Git (CI gates the commit; CD acts on the merged commit; Argo CD
deploys the resulting Git change). All third-party actions are pinned to immutable commit SHAs.

## To adapt them for your own repository

1. Create the Azure resources and the `id-ak-cicd` managed identity (see the
   [provisioning runbook](../../docs/guides/environment-provisioning-runbook.md)).
2. Add an OIDC **federated credential** whose subject names **your** repository and branch.
3. Set the repository **variables** `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID`.
4. Set the repository **secrets** `SONAR_TOKEN` and `CD_PUSH_TOKEN`, and point `SONAR_ORG` /
   `SONAR_PROJECT_KEY` at your own SonarCloud project.
5. Point `ACR_NAME` / `REGISTRY` at your own registry and grant the identity **AcrPush**.
6. Configure branch protection and add the `CD_PUSH_TOKEN` account to its bypass list.
