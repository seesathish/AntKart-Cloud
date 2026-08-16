# Argo CD — GitOps delivery for AntKart

This directory makes **Git the source of truth** for what runs in the AKS cluster. Instead of an operator running `helm upgrade` by hand, **Argo CD** runs inside the cluster, watches this repo, and reconciles the live state to match. The six services still deploy through the **same** generic chart (`deploy/helm/antkart-service`) with the **same** per-service values files (`deploy/helm/values/*.yaml`) — Argo CD just becomes the thing that runs the chart.

## What Argo CD is (pull-based GitOps)

Argo CD is a Kubernetes controller that continuously compares a **desired state** (Kubernetes manifests / a Helm chart in Git) against the **live state** in the cluster, and can converge one to the other.

- **Pull-based, not push-based.** The classic CI model *pushes* into the cluster: a pipeline holds cluster credentials and runs `helm`/`kubectl` from outside. Argo CD instead *pulls*: the controller lives **inside** the cluster and reads Git. No external system needs cluster credentials, and the cluster is never “applied to” by a laptop whose state nobody recorded. Git is the audit log — every change is a reviewed commit.
- **Desired state = this repo.** Each Argo CD **Application** points at a repo + path (+ Helm values) and a destination (cluster + namespace). Argo renders the chart (`helm template`) and diffs the result against what’s live.
- **Sync status:** `Synced` (live matches Git) or `OutOfSync` (they differ). **Health status:** `Healthy`/`Progressing`/`Degraded` (derived from the actual resources — e.g. a Deployment’s ready replicas).

## Contents

| File | What it is |
|------|------------|
| `appproject-antkart.yaml` | A dedicated **AppProject** scoping the Applications to this repo → the `antkart` namespace → namespaced resources only (least-privilege alternative to the built-in `default` project). |
| `applicationset-antkart.yaml` | **RECOMMENDED.** One **ApplicationSet** that templates all six Applications from a list — a single source of truth. |
| `applications/ak-*.yaml` | **ALTERNATIVE.** The same six as standalone **Application** manifests. Use these *or* the ApplicationSet, **never both** (identical names would collide). |

### ApplicationSet vs six standalone Applications

Use the **ApplicationSet**. The six services are near-identical — same repo, same chart, same project, same destination — differing only by name and values file. One template means one place to change shared behaviour (project, sync policy, repo URL) and no risk of six copies drifting apart; adding a service is a one-line list edit. The standalone Applications are included because a flat, per-service object is occasionally easier to hand-tweak or reason about in isolation, and they make the shape of a single Application obvious. Both are provided; pick one.

### Project: `antkart` (not `default`)

The manifests use a dedicated `antkart` **AppProject**. The built-in `default` project allows any repo → any cluster → any resource kind; that is fine for a throwaway experiment but has no guard rails. `antkart` restricts sources to this repo, destinations to the in-cluster `antkart` namespace, and resources to namespaced kinds only — so a mistyped or tampered Application can’t be repointed at another repo, deploy into `kube-system`, or create cluster-scoped objects (RBAC/CRDs). The cost is one extra manifest that must be applied **first**. If you want the absolute minimum, change `project: antkart` to `project: default` in the manifests and skip `appproject-antkart.yaml`.

### How the gateway’s `--set` overrides are preserved

Today the gateway is deployed with two command-line overrides on top of its values file:

```bash
helm upgrade --install ak-gateway deploy/helm/antkart-service \
  -n antkart -f deploy/helm/values/gateway.yaml \
  --set ingress.enabled=true \
  --set ingress.host=api.antkart.in
```

GitOps has no `--set` at sync time — every input must live in Git. Those two flags are translated **1:1** into the gateway’s Argo **Helm parameters**:

- In the **standalone Application** (`applications/ak-gateway.yaml`) they are an explicit `spec.source.helm.parameters` list:
  ```yaml
  helm:
    valueFiles: [ ../values/gateway.yaml ]
    parameters:
      - { name: ingress.enabled,      value: "true" }
      - { name: ingress.host,         value: "api.antkart.in" }
      - { name: ingress.clusterIssuer, value: "letsencrypt-prod" }
  ```
- In the **ApplicationSet**, an ApplicationSet template allows only string substitution (not per-element control flow), so every element carries the same `ingressEnabled`/`ingressHost` fields and the template renders them as the two parameters for all six. They are `"false"`/`""` for the five internal services — a no-op equal to the chart default — and only the `ak-gateway` element sets `"true"` / the real host `api.antkart.in`.

This was chosen over baking the values into `gateway.yaml` so that the values file stays largely unchanged and the manual `helm` flow in the [AKS Guide](../../docs/guides/aks-guide.md) keeps working (its `ingress.enabled: false` default still prevents a plain install from creating an ingress before the controller exists). The GitOps-specific enablement lives in the Argo manifest, which is the GitOps entry point.

`ingress.host` is the **external host** — the custom domain **`api.antkart.in`** (a GoDaddy A record points it at the ingress controller's LoadBalancer public IP `20.246.197.150`), committed in the manifests. Where no domain is available, a **`<public-ip>.nip.io`** value is the drop-in fallback (nip.io resolves any embedded IP with no DNS setup). Find the controller IP — to set the A record, or to build the nip.io fallback:

```bash
kubectl -n ingress-nginx get svc ingress-nginx-controller \
  -o jsonpath='{.status.loadBalancer.ingress[0].ip}{"\n"}'   # -> 20.246.197.150 (point api.antkart.in here, or use <ip>.nip.io)
```

> **Before enabling self-heal, reconcile the TLS issuer with live state.** `gateway.yaml` commits `clusterIssuer: letsencrypt-staging`. If the running gateway was already switched to `letsencrypt-prod` (the documented post-validation step), then Git says *staging* while the cluster runs *prod* — a manual sync will show that diff, and turning on `selfHeal` would revert prod → staging. Check with `kubectl -n antkart get ingress ak-gateway -o yaml` (and the issued cert); if live is prod, change `gateway.yaml` to `letsencrypt-prod` and commit **before** enabling self-heal.

---

## Prerequisites

- `kubectl` pointed at `aks-antkart-dev` with a working Entra login (see [AKS Guide → Operator Access](../../docs/guides/aks-guide.md#operator-access)).
- The six services already running in `antkart` (installed manually via Helm) — this directory **adopts** them.
- The Argo CD CLI (`argocd`) for the command-line flow. Install: `brew install argocd` (macOS), `choco install argocd-cli` (Windows), or download from the [releases page](https://github.com/argoproj/argo-cd/releases). Everything here can also be done from the web UI.

---

## 1. Install Argo CD into the cluster

```bash
kubectl create namespace argocd
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
```
- `create namespace argocd` — Argo CD runs in its own namespace and only reconciles `Application`/`ApplicationSet` objects that live there.
- `apply -n argocd -f <install.yaml>` — the **official** non-HA install manifest (`stable` = latest stable release). It creates the CRDs (`Application`, `ApplicationSet`, `AppProject`), the controllers (application-controller, applicationset-controller, repo-server), the API server, Redis, and Dex. Re-running it upgrades in place (idempotent). For a pinned version use `.../argo-cd/v2.13.2/manifests/install.yaml` instead of `stable`.

Wait for the pods to be ready:

```bash
kubectl -n argocd rollout status deploy/argocd-server --timeout=180s
kubectl -n argocd get pods
```
- `rollout status` blocks until the API server Deployment is available (or the timeout fires). `get pods` should show `argocd-application-controller`, `-applicationset-controller`, `-repo-server`, `-server`, `-redis`, `-dex-server` all `Running`.

### Reach the UI and get the admin password

```bash
kubectl -n argocd port-forward svc/argocd-server 8080:443
```
- Forwards local `8080` → the Argo CD server’s `443`. Open **https://localhost:8080** (self-signed cert → accept the browser warning). Leave this running in its own terminal.

```bash
# bash
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d; echo
```
```powershell
# PowerShell
$b64 = kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}"
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
```
- The initial admin password is auto-generated into the `argocd-initial-admin-secret` Secret; `-o jsonpath` extracts the base64 field and `base64 -d` decodes it. Username is **`admin`**. **Change it after first login** (`argocd account update-password`) and then delete the bootstrap secret: `kubectl -n argocd delete secret argocd-initial-admin-secret`.

Log in with the CLI (same credentials), against the port-forward:

```bash
argocd login localhost:8080 --username admin --password '<password-from-above>' --insecure
```
- `--insecure` accepts the self-signed cert on the port-forward. This lets you use `argocd app ...` commands below.

---

## 2. Git repo access (credential concern)

Argo CD’s repo-server needs **read** access to this Git repo to render the chart.

- **This repo is PUBLIC** (`https://github.com/seesathish/AntKart-Src3.git`) — verified reachable unauthenticated. **No credential is required**; Argo CD clones it anonymously. Nothing to configure. This is the case that applies today.
- **If the repo is ever made private**, register a read-only credential once (it never enters this repo):

  ```bash
  # Option A — HTTPS with a GitHub fine-grained PAT (Contents: Read-only)
  argocd repo add https://github.com/seesathish/AntKart-Src3.git \
    --username <github-username> --password <PAT>
  # Option B — declaratively, a Secret labelled for Argo CD (store via sealed-secrets / SOPS, not plain Git):
  #   kind: Secret, metadata.labels[argocd.argoproj.io/secret-type]=repository,
  #   stringData: { url, username, password }
  ```
  - `argocd repo add` stores the credential as a Secret in the `argocd` namespace, keyed by repo URL; the Application manifests are **unchanged** either way (the credential is matched by `repoURL`, not referenced in the Application). Use a least-privilege token (read-only, this repo only) and never commit the PAT.

---

## 3. Apply the AppProject and the Applications

**Apply the project first** (the Applications reference `project: antkart`):

```bash
kubectl apply -f deploy/argocd/appproject-antkart.yaml
```

Then **either** the ApplicationSet (recommended) **or** the standalone Applications — not both.

```bash
# RECOMMENDED — the ApplicationSet templates all six Applications:
kubectl apply -f deploy/argocd/applicationset-antkart.yaml

# ALTERNATIVE — the six standalone Applications:
kubectl apply -f deploy/argocd/applications/
```
- `apply -f <dir>` applies every manifest in the directory. The ApplicationSet controller expands `applicationset-antkart.yaml` into the six Applications automatically; you’ll see them appear with `kubectl -n argocd get applications`.

> **Gateway host.** The `ak-gateway` element/manifest already carries the live host `api.antkart.in` (kept in sync between `applications/ak-gateway.yaml` and `applicationset-antkart.yaml`). If you deploy into a different environment, change that one value first — to your own domain, or a `<public-ip>.nip.io` fallback (see the command above) — and commit **before** syncing `ak-gateway`, so it never renders an ingress for the wrong host.

Both delivery paths now commit `automated: { selfHeal: true, prune: false }` (the standalone Applications and the ApplicationSet template alike), so once applied, Argo CD **auto-syncs** Git changes and reverts live drift — `prune` stays off, so a Git deletion never cascades into the cluster. The manual `argocd app sync` steps in §5 remain the recommended **first** adoption of already-running services (diff before applying); to reproduce that cautious flow, temporarily disable automation with `argocd app set <name> --sync-policy none`.

---

## 4. View sync status

```bash
argocd app list                         # all Applications: SYNC + HEALTH at a glance
argocd app get ak-products              # one Application: per-resource sync/health + parameters
argocd app diff ak-products             # exactly what a sync WOULD change (live vs Git) — read this before syncing
kubectl -n argocd get applications      # the same, straight from the CRD
```
- `app list` is the fleet view. `app get` shows every resource the Application manages and why it’s Healthy/OutOfSync. **`app diff` is the safety step for adoption** — it prints the diff a sync would apply; for an already-correct service this should be empty or cosmetic (Argo tracking labels only). The UI shows the same as a resource tree.

---

## 5. Manual sync (the safe first adoption)

Sync one Application at a time and verify:

```bash
argocd app sync ak-products             # apply Git state for this one Application
argocd app wait ak-products --health    # block until it reports Healthy
```
- `app sync` performs a one-shot reconcile **now** (equivalent to clicking **Sync** in the UI). On first sync Argo adopts the existing, manually-installed resources (via `ServerSideApply=true`) and stamps them with its tracking label — normally a no-op change to the workload. `app wait --health` blocks until the resources are Healthy so you can gate the next step.

**Safest adoption order** (leaf/backing services first, the public edge last):

```
1. ak-discount     # no dependencies; TCP-probed gRPC
2. ak-products     # calls discount
3. ak-cart
4. ak-order        # calls products
5. ak-payments
6. ak-gateway      # the public entry point — adopt LAST, after its host is set and 1–5 are Synced/Healthy
```
Sync each, confirm `Synced` + `Healthy` and that traffic still flows, then proceed. Adoption applies the same chart the services already run, so a correctly-authored Application produces an empty diff and no restart — doing the gateway last keeps the external ingress untouched until every internal service is confirmed clean.

---

## 6. Enable auto-sync / self-heal / prune (when ready)

Only after all six are stably `Synced` + `Healthy` (and the issuer note above is reconciled), turn on automation in stages. Edit the `syncPolicy` in the ApplicationSet (or each Application) and commit:

```yaml
    syncPolicy:
      automated:
        selfHeal: true      # add in stage 2
        prune: true         # add in stage 3
      syncOptions:
        - CreateNamespace=false
        - ServerSideApply=true
```

- **Stage 1 — `automated: {}`** (empty block): Argo syncs **automatically whenever Git changes**. A merged commit to `master` rolls out on its own; live drift is still only *reported*, not corrected.
- **Stage 2 — add `selfHeal: true`**: if the **live cluster drifts** from Git (someone `kubectl edit`s a Deployment, or a controller mutates it), Argo **reverts it** back to Git. Git becomes truly authoritative.
- **Stage 3 — add `prune: true`**: if a resource is **removed from Git**, Argo **deletes it** from the cluster. Without prune, deletions in Git are ignored (orphans linger). Enable last — it’s the only one that *deletes* things.

Or toggle without editing Git, to trial it:

```bash
argocd app set ak-products --sync-policy automated              # stage 1
argocd app set ak-products --self-heal                          # stage 2
argocd app set ak-products --auto-prune                         # stage 3
argocd app set ak-products --sync-policy none                   # back to manual
```
- `app set` mutates the live Application’s sync policy immediately. Useful to experiment, but remember the ApplicationSet will re-assert whatever is in Git — for a lasting change, edit and commit the manifest.

---

## Uninstall / rollback

```bash
argocd app set ak-products --sync-policy none    # stop auto-sync first (avoid a fight)
kubectl delete -f deploy/argocd/applicationset-antkart.yaml   # remove the Applications (see note)
kubectl delete -f deploy/argocd/appproject-antkart.yaml
```
- The Applications carry the `resources-finalizer.argocd.argoproj.io` finalizer, so deleting an Application **also deletes the workloads it manages** (a cascading delete). To remove the *Argo tracking* but **keep the running services**, first remove the finalizer: `kubectl -n argocd patch app ak-products -p '{"metadata":{"finalizers":null}}' --type merge`, then delete. To revert a bad sync of a single app: `argocd app rollback <app> <history-id>` (`argocd app history <app>` lists them).

---

## See also

- [AKS Guide](../../docs/guides/aks-guide.md) — the manual Helm deployment this replaces, cluster/ingress/TLS.
- [Operations Command Reference](../../docs/guides/operations-command-reference.md) — every `kubectl`/`helm`/`az` command with flags explained.
- [deploy/helm/README](../helm/README.md) — the chart these Applications drive.
- [ADR-022](../../docs/adr/ADR-022-cicd-github-actions-oidc.md) — CI/CD + OIDC decision (GitOps is the delivery half).
