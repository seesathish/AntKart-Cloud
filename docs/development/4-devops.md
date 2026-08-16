# DevOps — how it ships

> **Diagrams pending review:** _Commit to running pod_, _CI pipeline_, _CD pipeline_, and _GitOps reconciliation_ are carried across as-is and will be reworked.

Delivery is a two-workflow pattern per service (twelve workflows across six services): **CI** on a pull request is the quality gate; **CD** on merge builds and delivers. CD never touches the cluster — it commits an image-tag change to Git, and **Argo CD** reconciles it. No cluster credentials live in CI/CD, and Azure auth is secret-less via OIDC.

## Commit to running pod

The whole loop at a glance; the three diagrams below are the detail.

```mermaid
flowchart TB
    DEV["Developer commit"]:::external
    PR["Pull request"]:::cicd
    CI["CI quality gate<br/>build · test · SonarCloud · Trivy<br/>4 required checks + branch protection"]:::cicd
    MERGE["Merge to master"]:::cicd
    OIDC{{"CD authenticates to Azure<br/>OIDC federated credential · no stored secret"}}:::identity
    BUILD["Build image · tag = commit SHA"]:::cicd
    ACR["Push to Azure Container Registry"]:::cicd
    BUMP["Bump image tag in Helm values in Git<br/>CD_PUSH_TOKEN · [skip ci]"]:::cicd
    ARGO["Argo CD detects drift"]:::cicd
    SYNC["Auto-sync + self-heal"]:::cicd
    POD["New pod on AKS"]:::service

    DEV --> PR --> CI --> MERGE --> OIDC --> BUILD --> ACR --> BUMP --> ARGO --> SYNC --> POD

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **One straight line, three concerns:** the pull-request **gate** (the CI pipeline), the merge-triggered **delivery** (the CD pipeline), and the in-cluster **reconciliation** (the GitOps loop) chain into a single path — nothing branches.
- **The gate is real:** merge is blocked until four required checks pass under branch protection (build-test, sonar, trivy, SonarCloud Code Analysis).
- **Secret-less and immutable:** CD authenticates by OIDC federated credential (no stored secret) and tags the image with the commit SHA.
- **Delivery is a Git commit, not a push to the cluster:** CD bumps the image tag in the Helm values with `CD_PUSH_TOKEN` and `[skip ci]`; Argo CD then detects drift and auto-syncs with self-heal.
- **Proven for all six services, hands-free** — a commit reaches a running pod with no manual step.

## CI pipeline

```mermaid
flowchart TB
    PR["Pull request → master<br/>path-filtered (service + BuildingBlocks + tests)"]:::cicd

    subgraph CI["service-ci.yml"]
        BT["job: build-test<br/>restore · build Release · unit + integration tests · OpenCover"]:::cicd
        SN["job: sonar (needs build-test)<br/>dotnet-sonarscanner begin/build/test/end"]:::cicd
        TR["job: trivy<br/>fs scan + Dockerfile scan · HIGH,CRITICAL · exit 1"]:::cicd
    end

    BP{{"Branch protection: master-protection<br/>required: build-test · sonar · trivy · SonarCloud Code Analysis"}}:::identity
    MERGE["Merge allowed only when all four checks are green"]:::cicd

    PR --> BT
    BT --> SN
    PR --> TR
    BT --> BP
    SN --> BP
    TR --> BP
    BP --> MERGE

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **Three jobs, real names:** `build-test`, `sonar`, `trivy` — exactly the job names in the workflow.
- **`sonar` depends on `build-test`** (`needs:`), so analysis is not spent on a commit that doesn't compile; `trivy` runs in parallel.
- **Four required checks, not three:** the `master-protection` ruleset requires `build-test`, `sonar`, `trivy`, **and** `SonarCloud Code Analysis` (SonarCloud's own PR quality-gate status).
- **CI is a pure gate:** it builds and tests but pushes no image and touches no cluster — delivery is the CD pipeline (below).
- **Path filters keep it per-service:** a PR touching only one service runs only that service's CI.

## CD pipeline

```mermaid
flowchart TB
    MERGE["Push to master · path-filtered<br/>service-cd.yml"]:::cicd

    subgraph J1["job: build-and-push"]
        TAG["compute tag = GITHUB_SHA first 7"]:::cicd
        OIDC{{"azure/login · OIDC · no secret<br/>id-ak-cicd-dev"}}:::identity
        BUILD["az acr login → docker build → push<br/>acrantkartdev/antkart/service:sha"]:::cicd
    end

    subgraph J2["job: update-gitops (needs build-and-push)"]
        CO["checkout master · token = CD_PUSH_TOKEN"]:::cicd
        YQ["yq: set .image.tag in deploy/helm/values/service.yaml"]:::cicd
        PUSH["commit + push · chore(cd) image → sha · [skip ci]"]:::cicd
    end

    ARGO["Argo CD reconciles"]:::cicd

    MERGE --> TAG --> OIDC --> BUILD --> CO --> YQ --> PUSH --> ARGO

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **Secret-less Azure auth:** `azure/login` uses OIDC federated with `id-ak-cicd-dev` (repo *variables*, not secrets) — the only Azure privilege is AcrPush.
- **Immutable tag:** the image is tagged with the **short commit SHA**, so a tag always means exactly one build.
- **Delivery is a Git commit, not a deploy:** job 2 bumps `.image.tag` in the service's values file and pushes — no `helm`/`kubectl`, no cluster credentials.
- **The push needs a PAT:** `update-gitops` checks out with `CD_PUSH_TOKEN` (the github-actions bot isn't a bypass actor), and the commit carries `[skip ci]`.
- **Loop-safe:** the tag-bump touches only `deploy/helm/values/**`, which is outside CD's path filter, so it can't retrigger CD.

## GitOps reconciliation

```mermaid
flowchart TB
    GIT[("Git · master<br/>deploy/helm/values/service.yaml")]:::cicd
    ARGO["Argo CD Application controller<br/>(in-cluster)"]:::cicd
    LIVE["Live cluster state<br/>Deployments / Services"]:::paas
    IMG["ACR image :sha"]:::cicd
    POLICY{{"syncPolicy on all six Apps<br/>automated · selfHeal: true · prune: false"}}:::identity

    GIT -->|watch| ARGO
    ARGO -->|compare desired vs live| LIVE
    ARGO -->|automated sync| LIVE
    LIVE -.->|drift detected| ARGO
    ARGO -->|selfHeal: revert to Git| LIVE
    LIVE -->|pull| IMG
    POLICY -.-> ARGO

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **Pull, not push:** Argo CD runs *inside* the cluster and watches Git — nothing outside the cluster deploys, and CD holds no cluster credentials.
- **Auto-sync closes the loop:** with `automated` on, the CD tag-bump commit deploys hands-free — no manual sync.
- **Self-heal makes Git authoritative:** live drift (a manual `kubectl edit`) is reverted back to Git; the sync policy is declared in Git on all six Applications.
- **Prune is off by design (`prune: false`):** Argo won't delete resources that disappear from Git — a conservative default recorded in the manifests.
- **The image is pulled by tag:** the Deployment references `:sha`, so the new pod runs exactly the image the commit named.

## How it was built

- The journey from code change to running pod, the per-service workflow layout, the SonarCloud/Trivy gates, branch protection, and OIDC/secrets handling: [DevOps CI/CD Guide](../guides/devops-cicd-guide.md).
- The DevOps area index: [DevOps Guide](../guides/devops-guide.md).

## Decisions

- [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](../adr/ADR-022-cicd-github-actions-oidc.md)
- [ADR-023 — CI/CD Pipeline Design and Repository Strategy](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md)

## Open items

- [KI-004 — mutable image tag can serve a stale image](../KNOWN_ISSUES.md): mitigated by the commit-SHA tags this pipeline uses. See also [KI-006 (resolved)](../KNOWN_ISSUES.md) — the CD path filters now exclude markdown so documentation changes don't trigger delivery.
