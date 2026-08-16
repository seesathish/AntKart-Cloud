# ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials to Azure

**Status:** Accepted
**Date:** 2026-07-23
**Area:** DevOps & DevSecOps
**Relates to:** ADR-012 (Infrastructure as Code with Terraform and Terragrunt), ADR-016 / ADR-018 (workload identity)

---

## Context

The platform needs a continuous integration and delivery system to build and test the .NET services, produce and push container images to the Azure Container Registry, and deliver those images onto the managed Kubernetes cluster. An earlier working assumption was that this pipeline would run on Azure DevOps Pipelines.

Two facts drove a re-evaluation of that assumption:

- **The source of truth already lives in GitHub.** The repository, pull requests, and code review all happen there. A CI/CD system co-located with the code removes a second platform, a second identity surface, and the cross-platform wiring (service connections, mirrored repositories) that keeping them apart would require.
- **The platform is deliberately secret-less.** Every runtime component authenticates to Azure through Microsoft Entra with `DefaultAzureCredential` and — inside the cluster — workload identity, so that no credential is stored, rotated, or committed. A CI/CD system that authenticated to Azure with a stored service-principal secret would be the one place that broke that posture.

The decision is which CI/CD platform to adopt, and how it authenticates to Azure.

## Decision

**Adopt GitHub Actions as the CI/CD platform, authenticating to Azure with OIDC federated credentials (workload identity federation) — no stored cloud credentials.**

- **GitHub Actions** runs the build, test, image-build, image-push, and deployment workflows, defined as workflow files committed alongside the code.
- **Authentication to Azure uses OpenID Connect federation.** A federated credential on an Entra application (or user-assigned managed identity) trusts GitHub's OIDC issuer, scoped to a specific repository, branch, and environment. At run time the workflow requests a short-lived GitHub OIDC token and exchanges it for an Entra access token; **no client secret or long-lived credential is stored in GitHub**. This mirrors the workload-identity model the cluster uses for runtime access.
- **Least-privilege, environment-scoped.** Federated credentials are scoped per environment so a workflow targeting one environment cannot obtain tokens for another, and the federated identity is granted only the roles its stage needs (push to the registry, deploy to the cluster).

This replaces the previously assumed Azure DevOps Pipelines.

## Considered Alternatives

### Alternative 1 — Azure DevOps Pipelines

A separate Azure DevOps project hosting the pipelines, connected to the GitHub repository, authenticating through a service connection (optionally itself using workload identity federation).

**Not chosen** because it introduces a second platform and identity surface alongside GitHub for no benefit while the code, reviews, and issues already live in GitHub. It remains a perfectly valid choice — see the trade-off below.

### Alternative 2 — GitHub Actions with a stored service-principal secret

The same platform, but authenticating to Azure with a client secret held as a GitHub Actions secret.

**Rejected** because a stored secret must be rotated, can leak, and is exactly the credential the OIDC federation model removes. It would be the single component contradicting the platform's secret-less posture.

## Consequences

**Positive**

- **One platform** for code, review, and delivery — no cross-platform service connections or mirrored repositories to maintain.
- **Secret-less authentication to Azure** — short-lived OIDC tokens exchanged for Entra tokens; nothing to rotate, leak, or commit. Consistent with the platform-wide `DefaultAzureCredential` / workload-identity model.
- **Environment-scoped least privilege** — federated credentials bound to repository, branch, and environment, each granted only the roles its stage requires.

**Trade-offs**

- **Azure DevOps remains common in enterprise environments.** Many organisations standardise on Azure DevOps for boards, pipelines, and artifacts, and teams there may expect it. This is a genuine trade-off, not a dismissal of the alternative.
- **The concepts transfer directly.** Pipelines, stages, environment approvals, and OIDC-based workload identity federation exist on both platforms; the skills and the security model carry over, so the choice is a platform preference rather than a lock-in of knowledge.
- **Action supply chain must be managed.** Third-party Actions are a supply-chain surface; they are pinned to a reviewed commit rather than a floating tag.

## Notes

The concrete workflow definitions, stages, and security gates are documented in the [DevOps Guide](../guides/devops-guide.md) as that phase is delivered. This ADR records only the platform-and-authentication decision.
