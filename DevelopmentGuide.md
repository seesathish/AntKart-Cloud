# Development Guide

How AntKart is built, layer by layer. Each link opens one section — read them in order or jump to what you need.

- [Platform architecture](docs/development/0-platform-architecture.md) — how the code is built: Clean Architecture, CQRS, the orchestrated SAGA, and the outbox.
- [Infrastructure as code](docs/development/1-infrastructure-as-code.md) — how the cloud gets built: Terraform modules and Terragrunt live units.
- [Azure services](docs/development/2-azure-services.md) — where it runs: the managed services and the network path to the edge.
- [Kubernetes](docs/development/3-kubernetes.md) — how it is orchestrated: AKS, the Helm chart, ingress/TLS, and workload identity.
- [DevOps](docs/development/4-devops.md) — how it ships: the CI gate, the CD pipeline, and GitOps reconciliation.
- [Observability](docs/development/5-observability.md) — how it is seen: structured logging today, metrics and tracing planned.
- [Security](docs/development/6-security.md) — how it is trusted: the secret-less identity chain, defence in depth, and the planned edge.
