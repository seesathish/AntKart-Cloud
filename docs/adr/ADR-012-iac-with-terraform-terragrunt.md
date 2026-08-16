# ADR-012 — Infrastructure as Code with Terraform and Terragrunt

**Status:** Accepted
**Date:** 2026-05-29
**Author:** Sathish Chandrakumar

---

## Context

AntKart Phase 1 ran entirely on Docker Compose on a local developer machine. Phase 2 moves the platform to Azure with AKS, Azure-managed databases, and a CI/CD pipeline. This requires a strategy for provisioning and managing cloud infrastructure.

The requirements that drove this decision:

1. **Reproducibility** — dev, staging, and prod environments must be provisionable from the same codebase with different configuration values, not manual portal steps.
2. **Team safety** — multiple developers must be able to make infrastructure changes without corrupting shared state or accidentally deleting production resources.
3. **Teaching value** — the chosen toolchain must be widely adopted in industry so that skills built on AntKart transfer to real-world Azure projects.
4. **DRY module structure** — the eight microservices and their dependencies create significant infrastructure. Backend configuration and provider setup should not be copy-pasted into every module folder.
5. **State isolation** — each logical component (resource group, networking, AKS, ACR, Key Vault, etc.) must manage its own state independently so that a plan for networking doesn't show every resource on the platform.

---

## Decision

Use **Terraform** (HashiCorp, open source) as the Infrastructure as Code engine and **Terragrunt** (Gruntwork, open source) as the Terraform orchestration wrapper.

### Repository structure

```
infrastructure/
├── terragrunt.hcl                    ← Root: backend + provider (once, shared)
├── modules/                          ← Reusable, environment-agnostic modules
│   ├── resource-group/
│   ├── networking/
│   ├── acr/
│   ├── aks/
│   ├── key-vault/
│   └── ...
└── environments/
    ├── dev/
    │   ├── env.hcl                   ← Dev-specific values (region, tags, CIDR)
    │   ├── resource-group/
    │   │   └── terragrunt.hcl        ← Wires module + env values + dependencies
    │   └── networking/
    │       └── terragrunt.hcl
    ├── staging/
    │   └── env.hcl
    └── prod/
        └── env.hcl
```

Each `environments/{env}/{module}/terragrunt.hcl` file:
- Includes the root config (backend + provider, inherited)
- Includes `env.hcl` (environment values, exposed as locals)
- Points at the module source
- Passes environment-specific inputs

### Root config filename

We name the root Terragrunt configuration file `root.hcl`, not `terragrunt.hcl`.

Newer versions of Terragrunt emit a deprecation warning when the root config is named `terragrunt.hcl`:

> "Using terragrunt.hcl as the root of Terragrunt configurations is an anti-pattern, and no longer recommended."

The `root.hcl` convention removes the ambiguity between the two roles a `.hcl` file can play:

| File | Role |
|------|------|
| `infrastructure/root.hcl` | One file, shared by all modules — backend + provider config |
| `environments/{env}/{module}/terragrunt.hcl` | Per-module file — wires a specific module to an environment |

Because `find_in_parent_folders()` with no argument defaults to searching for `"terragrunt.hcl"`, child modules must pass the filename explicitly:

```hcl
include "root" {
  path = find_in_parent_folders("root.hcl")
}
```

Without the explicit argument, `find_in_parent_folders()` would match the nearest per-module `terragrunt.hcl` instead of the root config — a subtle bug that would only surface when running from a nested module directory.

### Remote state

Terraform state is stored in Azure Blob Storage (`stantkarttfstate` / container `tfstate`). Each module gets its own state key derived from its folder path via `path_relative_to_include()`. State locking uses Azure Blob Storage's native lease mechanism.

### Authentication

All credential configuration uses `ARM_*` environment variables. No secrets are present in any file tracked by Git. The `antkart-terraform-sp` Service Principal has Contributor scope on the AntKart subscription.

---

## Alternatives Considered

### Option A: Azure Bicep (rejected)

Bicep is Microsoft's first-party IaC language for Azure. It has excellent Azure tooling support (VS Code extension, arm-ttk validation, What-If deployments).

**Reasons not chosen:**
- **Azure-only** — skills and modules are not transferable to multi-cloud or hybrid scenarios. The AntKart platform may need AWS or on-premises connectivity in Phase 3.
- **No Terragrunt equivalent** — Bicep has modules but lacks a DRY orchestration layer comparable to Terragrunt's `include` and `dependency` patterns. Each deployment unit would need its own redundant backend configuration.
- **Smaller ecosystem** — Terraform has a broader library of community modules and more StackOverflow coverage, which lowers the learning curve for developers adopting it.
- **State management** — Bicep uses ARM deployments (server-side history) rather than a local state file. This is simpler for basic cases but gives less control over drift detection and state manipulation.

### Option B: ARM Templates (rejected)

ARM Templates are Azure's original JSON-based IaC format.

**Reasons not chosen:**
- Verbose JSON syntax with no abstraction primitives makes complex infrastructure difficult to read and maintain.
- No local state — relies entirely on Azure deployment history, which is harder to diff and audit.
- Superseded by Bicep for new projects; Microsoft itself recommends Bicep or Terraform over raw ARM for new development.

### Option C: Pure Terraform (no Terragrunt) (considered, not chosen)

Terraform alone can achieve everything in this ADR. Terragrunt is optional.

**Reasons Terragrunt was added:**
- Without Terragrunt, every module folder needs its own `backend.tf` and `provider.tf` with identical content. With 6 environments × 10 modules = 60 copy-pasted backend blocks — a maintenance nightmare.
- Terragrunt's `dependency` block provides clean module-to-module output references without sharing state files.
- `run-all` enables deploying or destroying an entire environment with one command while respecting dependency order.
- The overhead is minimal: Terragrunt is a thin wrapper that calls vanilla Terraform. Every `terragrunt init/plan/apply` is exactly `terraform init/plan/apply` with injected configuration.

**Accepted tradeoff:** Adds a second tool to learn. Mitigated by explaining Terragrunt's role clearly (as this ADR and `DevelopmentGuide.md` do) and by the fact that the skill is widely used across the industry and transfers directly to other projects.

### Option D: Pulumi (rejected)

Pulumi allows writing infrastructure in TypeScript, Python, Go, or C# — which is attractive given the team's C# background.

**Reasons not chosen:**
- **Maturity** — Terraform's azurerm provider is significantly more mature and complete than Pulumi's Azure Native provider. Edge cases and uncommon resources are better supported.
- **Community** — Terraform has a vastly larger community, more example repositories, and more third-party modules.
- **Teaching goal** — The goal is to upskill on industry-standard tooling. Terraform is used in the majority of enterprise Azure environments; Pulumi is still niche in comparison.
- **Future consideration** — Pulumi remains a strong contender if the team needs to express complex infrastructure logic in C#. It can be re-evaluated in Phase 4.

---

## Consequences

### Positive

- **Single source of truth** — the Git repository is the authoritative record of infrastructure. Every change is a PR, every PR has a plan, every merge is an apply.
- **Environment parity** — dev, staging, and prod are provisionable from identical module code with different `env.hcl` values. Configuration drift is detected by Terraform plan.
- **Blast radius isolation** — module-level state files mean a plan for AKS shows only AKS resources. Destroying the AKS module doesn't touch the VNet or Key Vault state.
- **Skill transfer** — Terraform + Terragrunt skills are directly applicable to most Azure, AWS, and GCP projects encountered in industry.
- **Lifecycle protection** — `prevent_destroy = true` on the resource group prevents accidental deletion via `terraform destroy`, adding a safety layer that portal-based management lacks.

### Negative

- **Learning curve** — Developers new to Terraform need time to understand state, providers, the plan/apply cycle, and HCL syntax. Mitigated by `DevelopmentGuide.md` and heavy commenting in all `.tf` files.
- **Two tools instead of one** — Terragrunt adds a layer on top of Terraform. Debugging requires understanding where Terragrunt ends and Terraform begins (the `.terragrunt-cache` folder). Mitigated by comments in `terragrunt.hcl` files explaining what Terragrunt injects.
- **State bootstrap** — The tfstate storage account and resource group cannot be managed by Terraform (circular dependency). They must be pre-created by hand or a separate bootstrap script. Documented in `DevelopmentGuide.md`.
- **Provider version pinning** — Using `~> 4.0` allows minor version updates automatically. A provider update could introduce breaking changes. Mitigated by running `terragrunt plan` before every `apply` and reviewing diffs.

---

## References

- [Terraform azurerm provider documentation](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs)
- [Terragrunt documentation](https://terragrunt.gruntwork.io/docs/)
- [Azure Cloud Adoption Framework — Naming conventions](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming)
- [Azure CAF — IP addressing](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/plan-for-ip-addressing)
- [AKS network concepts — Azure CNI](https://learn.microsoft.com/en-us/azure/aks/concepts-network-azure-cni)
- ADR-001 through ADR-011 — Phase 1 architecture decisions in `docs/adr/`
