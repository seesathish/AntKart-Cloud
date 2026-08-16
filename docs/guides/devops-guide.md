# DevOps Guide

> **Status: in progress.** The CI/CD half is now being delivered service by service — see the **[DevOps CI/CD Guide](devops-cicd-guide.md)** for the pipeline design, the per-service workflow layout, the SonarCloud/Trivy PR gates, branch protection, and OIDC/secrets handling. The observability half is written as that phase is delivered.

**Purpose:** Continuous integration and delivery, security and compliance gates (DevSecOps), and end-to-end observability for the platform.

**What it covers:**

- **CI/CD** — the pipeline that builds, tests, and delivers each service, with the security gates (SonarCloud static analysis, Trivy scanning) that keep releases safe, and GitOps delivery through Argo CD. Established with Products first: **[DevOps CI/CD Guide](devops-cicd-guide.md)** · [ADR-023](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md).
- **Observability** _(upcoming)_ — the logging, metrics, and tracing that make the running platform observable end to end; see the [Observability design](../guides/observability-concepts.md).

For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md).
