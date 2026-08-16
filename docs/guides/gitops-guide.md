# GitOps Guide — Argo CD

This guide covers how the AntKart cluster is driven from Git with **Argo CD**. It is the conceptual and procedural companion to the manifest-level runbook in [deploy/argocd/README](../../deploy/argocd/README.md): read this to understand *what GitOps is and why*, then use the README as the reference for the manifests themselves.

The platform is now **Git-driven**: the desired state of every service in the cluster lives in this repository, and Argo CD continuously reconciles the running cluster to match it. Deploying a change is a `git push`, not a `helm`/`kubectl` command run from a laptop.

---

## What GitOps is (the pull model)

GitOps makes a Git repository the **single source of truth** for the desired state of a system, and puts a controller **inside** the target environment to converge the live state onto that desired state.

- **Declarative desired state in Git.** What should run — here, the generic Helm chart plus per-service values — is expressed declaratively and version-controlled. Every change is a reviewed, attributable commit, so Git doubles as the audit log and the rollback point.
- **Pull, not push.** The classic CI model *pushes*: a pipeline outside the cluster holds cluster credentials and runs `helm`/`kubectl` inward. GitOps *pulls*: a controller running in the cluster reads Git and applies changes from the inside. No external system holds cluster credentials, and the cluster is never mutated by an out-of-band command whose effect no one recorded.
- **Continuous reconciliation.** The controller constantly compares desired (Git) against live (cluster). Divergence is **drift**; the controller can report it, and — once configured — automatically correct it.

The unit of comparison is **sync status**: `Synced` (live matches Git) or `OutOfSync` (they differ). Orthogonal to it is **health**: `Healthy` / `Progressing` / `Degraded`, derived from the actual resources (for a Deployment, whether its replicas are ready).

---

## Why Argo CD

Argo CD is a Kubernetes-native GitOps controller. It fits the platform because:

- **The source of truth is Git.** The same chart and values files already used for manual Helm deployment become the desired state — no second description of the system to keep in sync.
- **Drift detection.** Argo CD surfaces any divergence between Git and the cluster, whether introduced by a manual `kubectl edit`, a controller, or an incomplete rollout.
- **Self-heal.** When enabled, Argo CD reverts live drift back to Git automatically, making Git genuinely authoritative rather than merely advisory.
- **Deploy = `git push`.** A merged commit rolls out on its own once automated sync is on. There is no deploy step that lives outside version control.
- **No external credential custody.** The controller runs in-cluster and reads Git (a public repository here, so no Git credential at all). Nothing outside the cluster needs cluster-admin credentials to deploy.

Argo CD complements, rather than replaces, the Helm chart: it is the thing that *runs* the chart. The [AKS Guide](aks-guide.md) manual `helm upgrade` flow still works unchanged; Argo CD renders the identical chart and applies the result.

---

## The structure in `deploy/argocd`

Three object types express the whole setup. Full manifests and rationale are in [deploy/argocd/README](../../deploy/argocd/README.md); the roles are:

- **AppProject (`appproject-antkart.yaml`).** A dedicated `antkart` project that scopes what its Applications may do — restricting the **allowed source repository**, the **destination namespace** (`antkart`), and the **resource scope** (namespaced kinds only, no cluster-scoped objects). It replaces the built-in `default` project, which has no such guard rails, so a mistyped or tampered Application cannot be repointed at another repository, deploy into `kube-system`, or create cluster-scoped resources.
- **Application (one per service).** Each of the six services has an Application pointing at `deploy/helm/antkart-service` with its per-service values file (`../values/<service>.yaml`) and a destination of the in-cluster `antkart` namespace. The gateway additionally carries its ingress overrides (`ingress.enabled` / `ingress.host` / issuer) as Application **Helm parameters**, so the GitOps-rendered manifests match the running state without editing the shared values file.
- **ApplicationSet (`applicationset-antkart.yaml`).** An alternative that templates all six Applications from a single list — one place to change shared behaviour, no risk of six copies drifting apart. Apply the ApplicationSet **or** the six standalone Applications, never both (identical names collide).

---

## Install and adoption — command sequence

Every flag is explained inline. Commands assume `kubectl` is pointed at `aks-antkart-dev` with a working Entra login (see [AKS Guide → Operator Access](aks-guide.md#operator-access)).

### 1. Install Argo CD

```bash
kubectl create namespace argocd
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
kubectl -n argocd rollout status deploy/argocd-server --timeout=180s
```
- `create namespace argocd` — Argo CD runs in, and only reconciles Application/ApplicationSet objects from, its own namespace.
- `apply -n argocd -f <install.yaml>` — the official non-HA install manifest (`stable` = latest stable release). It creates the CRDs (`Application`, `ApplicationSet`, `AppProject`), the controllers, the API server, Redis, and Dex. Re-running it upgrades in place. Pin a version with `.../argo-cd/<version>/manifests/install.yaml` instead of `stable`.
- `rollout status ... --timeout=180s` — blocks until the API-server Deployment is available, or fails after 180 seconds.

### 2. Reach the UI and get the admin password

```bash
kubectl -n argocd port-forward svc/argocd-server 8080:443
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d; echo
```
- `port-forward svc/argocd-server 8080:443` — maps local `8080` to the Argo CD server's `443`; browse **https://localhost:8080** (self-signed cert → accept the warning). Leave it running in its own terminal.
- `get secret ... -o jsonpath=... | base64 -d` — the initial password is auto-generated into `argocd-initial-admin-secret`; `-o jsonpath` selects the base64 field and `base64 -d` decodes it. Username is `admin`. Change it after first login (`argocd account update-password`), then delete the bootstrap secret. (PowerShell decode: `[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))`.)

### 3. Apply the project, then the Applications — order matters

```bash
kubectl apply -f deploy/argocd/appproject-antkart.yaml          # 1. the project FIRST
kubectl apply -f deploy/argocd/applicationset-antkart.yaml      # 2. then the Applications (ApplicationSet)
#   OR: kubectl apply -f deploy/argocd/applications/            #    the six standalone Applications
```
- **Apply the AppProject before any Application.** Each Application declares `project: antkart`; applying an Application before its project exists is rejected with **`application is not allowed in project antkart`** (Argo CD validates project membership on admission). Applying the project first avoids it.
- The ApplicationSet controller expands `applicationset-antkart.yaml` into the six Applications automatically; watch them appear with `kubectl -n argocd get applications`.
- **Note on sync policy:** the committed manifests now declare **auto-sync** (`automated: { selfHeal: true, prune: false }`) on all six Applications, so applying them and pushing a change deploys hands-free. The manual-sync steps below describe the **safe first-adoption** approach used historically (diff before applying); to reproduce it, temporarily set the Application's sync policy to manual (`argocd app set <app> --sync-policy none`) before syncing.

> The gateway's `ingress.host` is the external host — the custom domain **`api.antkart.in`** (GoDaddy A record → the ingress controller's public IP), set in the `ak-gateway` Application (keep it in sync with the ApplicationSet element). Its issuer (`letsencrypt-prod`) comes from `deploy/helm/values/gateway.yaml`. Both are driven from Git — with auto-sync + self-heal now enabled on all six Applications, a `helm --set` would be reverted; change the host/issuer in Git instead. See [deploy/argocd/README](../../deploy/argocd/README.md).

### 4. Read the diff, then sync manually

```bash
argocd app list                     # every Application: SYNC + HEALTH at a glance
argocd app diff ak-products         # exactly what a sync WOULD change (live vs Git) — read before syncing
argocd app sync ak-products         # apply Git state for this one Application, now
argocd app wait ak-products --health  # block until it reports Healthy
```
- `app diff` is the safety step: it prints the change a sync would apply. For an already-correctly-deployed service this is empty or cosmetic. **In this platform's adoption the only diff was Argo CD's own tracking-id annotation** — Argo stamping the resources it now manages, with no change to the running workloads.
- `app sync` performs a one-shot reconcile now (the CLI equivalent of clicking **Sync** in the UI). `app wait --health` gates the next step on the resources becoming Healthy.

**Adopt with manual sync first, one service at a time.** Starting without automation lets you observe drift and read each diff before anything is enforced — the safe way to bring already-running services under Argo CD's management. Sync leaf/backing services first and the public gateway last, confirming `Synced` + `Healthy` at each step. In this platform, all six Applications reached `Synced` + `Healthy` with no workload change.

### 5. Enable automated sync, self-heal, and prune (staged)

Only once every Application is stably `Synced` + `Healthy`, turn on automation in stages by editing the `syncPolicy` in Git and committing:

```yaml
    syncPolicy:
      automated:
        selfHeal: true      # add in stage 2
        prune: true         # add in stage 3
      syncOptions:
        - CreateNamespace=false
        - ServerSideApply=true
```

- **Stage 1 — `automated: {}`** (empty block): Argo CD syncs **automatically whenever Git changes**. A merged commit rolls out on its own; live drift is still only reported.
- **Stage 2 — `selfHeal: true`**: if the **live cluster drifts** from Git (a manual `kubectl edit`, a mutating controller), Argo CD **reverts it** to match Git. Git becomes authoritative.
- **Stage 3 — `prune: true`**: if a resource is **removed from Git**, Argo CD **deletes it** from the cluster. Enable last — it is the only one that deletes.

To trial a policy on the live object without editing Git (the ApplicationSet re-asserts Git on the next reconcile, so this is temporary):

```bash
argocd app set ak-products --sync-policy automated   # stage 1
argocd app set ak-products --self-heal               # stage 2
argocd app set ak-products --auto-prune              # stage 3
argocd app set ak-products --sync-policy none        # back to manual
```
The equivalent low-level operation is a patch of the Application's `spec.syncPolicy` (see the [Operations Command Reference](operations-command-reference.md#m-argo-cd-gitops)).

---

## Verified on the cluster

- **Adoption was non-disruptive.** All six Applications reached `Synced` + `Healthy`. The only difference a sync applied was Argo CD's tracking-id annotation on the adopted resources — the running workloads were unchanged.
- **Deploy = `git push`, proven.** Changing `replicaCount` in a service's values file, committing, and pushing caused Argo CD to report the Application `OutOfSync`; after a sync it scaled the Deployment's ReplicaSet from 1 to 2 — with no `helm` or `kubectl` command issued against the cluster.
- **Apply order confirmed.** Applying an Application before the `antkart` AppProject is rejected with `application is not allowed in project`; applying the project first resolves it.
- **Kubernetes reconciliation, independently.** After a cluster stop and redeploy, three services (Order, Payments, Discount) initially entered `CrashLoopBackOff` because PostgreSQL was stopped, and **recovered automatically once the database was started** — no pod intervention. This is the Kubernetes control loop restarting failed pods until their dependency returned; it is distinct from Argo CD self-heal (which reconciles Git-vs-cluster drift, not runtime failures).

---

## See also

- [deploy/argocd/README](../../deploy/argocd/README.md) — the manifests, the gateway-override handling, the credential note, and the uninstall/rollback path.
- [Operations Command Reference → Argo CD](operations-command-reference.md#m-argo-cd-gitops) — the Argo CD command set with every flag explained, and the session bring-up/shutdown routine.
- [AKS Guide](aks-guide.md) — the manual Helm deployment Argo CD now drives, plus the cluster, ingress, and TLS.
- [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md) — CI/CD and OIDC decision; GitOps is the delivery half of it.
