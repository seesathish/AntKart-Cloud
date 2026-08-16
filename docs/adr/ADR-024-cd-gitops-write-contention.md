# ADR-024 — CD GitOps Write Contention on the Tag-Bump Push

**Status:** Accepted
**Date:** 2026-08-01
**Area:** DevOps & DevSecOps
**Relates to:** ADR-023 (CI/CD pipeline design and repository strategy), ADR-022 (CI/CD on GitHub Actions with OIDC federated credentials to Azure), ADR-020 (managed edge gateway / GitOps delivery)

---

## Context

ADR-023 established GitOps delivery: each service's CD workflow, on merge to `master`, builds an immutable image, pushes it to the registry, and then **writes the new image tag back into `master`** (`deploy/helm/values/<service>.yaml`) for Argo CD to reconcile. The final act of every CD run is a `git push origin HEAD:master`.

Because CD's trigger paths include the shared library (`AK.BuildingBlocks/**`), a single change to BuildingBlocks trips **all six** services' path filters, so **all six CD workflows run at once**. Each `update-gitops` job checks out `master`, bumps its own values file with `yq`, commits, and pushes. All six branch from the **same** commit and push in parallel.

**Proven failure — PR #11, merge commit `cbed6c0`.** The first pushes fast-forward and win; the rest are rejected as non-fast-forward, and the job fails with **no retry**. On PR #11, products/shoppingcart/discount/payments won; **gateway and order lost**. Their images had been built and pushed to ACR, but their values files still pointed at the previous tag, so Argo CD never rolled them — **gateway was left running stale code**. Manual "Re-run failed jobs" recovered it. The failure is intermittent: PR #10 happened to have all six land cleanly, purely on timing.

**Root cause.** Each workflow declares a concurrency group scoped to **itself** (`group: <service>-cd`), which only serialises a service against its own runs. The actually contended resource is the **`master` branch** — and nothing guards that **across** workflows. More fundamentally, the deployment manifests live in the **same repository as the source**, so CD writes back to the very repository whose merge triggered it, and the six runs inherently contend with one another for the branch tip.

## Decision

**Rebase-and-retry on the tag-bump push, with a full-history checkout and a post-push verification step.** Applied identically to all six CD workflows.

1. **Full-history checkout.** The `update-gitops` checkout adds `fetch-depth: 0`. The default shallow clone (depth 1) makes `git pull --rebase` unreliable; a rebase needs the real history.
2. **Bounded rebase-and-retry push.** Replace the bare `git push origin HEAD:master` with a loop of up to **five** attempts: on a rejected push, `git pull --rebase origin master` and retry. Each service touches a **different** values file, so a rebase onto the latest `master` is **always conflict-free** — it simply replays this service's one-line change on top of whichever sibling landed first. If all five attempts fail, the job **fails loudly** with an error.
3. **Post-push verification.** A new step fetches `master` fresh and asserts the remote values file actually contains the tag that was just built, failing the job otherwise — so a silent no-op can never masquerade as success.

The early-exit when there is nothing to commit, the `[skip ci]` suffix, and the existing bot `user.name`/`user.email` are kept unchanged.

## Considered Alternatives

### Shared concurrency group across the six workflows (rejected)

The obvious "serialise the pushes" fix is to give all six workflows the **same** concurrency group so only one runs at a time. It is rejected because of GitHub's concurrency semantics: a concurrency group holds **at most one pending run**. With six racing, the first runs, the second queues — and when a **third** arrives it **cancels** the queued one. The result is that tag-bumps are **silently dropped**: a cancelled run leaves no failed check to notice and nothing to re-run. That is strictly **worse** than the current failure mode, which is loud and re-runnable. A visible, retryable failure is preferable to a silent loss of a deployment.

The existing **per-service** concurrency groups (`group: <service>-cd`) are **kept** — they serve a different, valid purpose (never overlap a service with its own newer run) and do not contend across services.

### Separate GitOps / configuration repository (rejected for now — the correct long-term answer)

The root cause is architectural: **deployment manifests live in the same repository as application source**, so CD writes back to the repository that triggered it and inherently contends with itself. The standard resolution is a **dedicated configuration repository** that Argo CD watches and CD writes to — application CI/CD and delivery state become fully decoupled, the app repo never receives automated commits, and cross-service tag-bumps no longer race on the app branch. ADR-023 already records this as an evolution path.

It is **deferred** because it is a **delivery-model change, not a defect fix**: a new repository, new Argo CD `Application` sources, new write credentials, and a migration of every values file — disproportionate to closing an intermittent race. Rebase-and-retry is recorded here as the **deliberate interim**; the separate config repository remains the correct long-term direction.

## Consequences

**Positive**

- **Builds stay parallel.** The `build-and-push` jobs are untouched; only the **seconds-long push** serialises, and only when two land at once — the common single-service change never rebases at all.
- **No more silent stale images.** A lost race now self-heals via rebase instead of failing; if a push genuinely cannot land, the job **fails loudly after five attempts** rather than leaving a service on a stale tag, and the verification step guarantees the tag that was built is the tag on `master`.
- **No new infrastructure.** The fix is contained to the six workflow files — no new repository, credentials, or Argo CD wiring.

**Trade-offs**

- **Bounded, not unbounded.** Five attempts is a deliberate ceiling; a pathological, sustained contention would still fail (loudly) rather than retry forever.
- **The architectural contention remains.** This narrows the race to a near-impossible window but does not remove the underlying same-repo write-back; the separate configuration repository is still the real fix when the delivery model is next revisited.

## Notes

The concrete push loop and verification step live in the six `.github/workflows/<service>-cd.yml` `update-gitops` jobs; the tag-bump mechanism and its branch-protection interaction are described in the [DevOps CI/CD Guide](../guides/devops-cicd-guide.md). This ADR refines the delivery mechanism ADR-023 established; the parallel-per-service pipeline shape it builds on is unchanged.
