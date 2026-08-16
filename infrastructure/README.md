# Infrastructure

This directory holds the Infrastructure-as-Code for the platform's cloud environment, provisioned with **Terraform** and **Terragrunt**.

- **New to Terraform/Terragrunt?** Start with the [IaC fundamentals primer](../docs/guides/iac-concepts.md).
- **Want the full step-by-step walkthrough?** See the [Infrastructure Guide](../docs/guides/infrastructure-guide.md) (each resource as Understand → Build → Execute → Verify, plus a diagnostics playbook).

## Layout — modules vs. environments

```
infrastructure/
├── modules/            Reusable Terraform modules — one per resource type (HOW)
└── environments/
    └── dev/            Terragrunt "live" units — one per deployed thing (WHAT)
        └── <unit>/terragrunt.hcl
```

- **`modules/`** — reusable, parameterised Terraform modules (resource group, networking, container registry, key vault, observability, cosmosdb, postgresql, redis, servicebus, eventgrid, communication-services, function-app, app-registration, role-assignments, governance, **aks**, **workload-identity**, **github-oidc**). A module describes *how* a resource is built — it takes inputs and exposes outputs, with **no environment-specific values baked in**.
- **`environments/dev/`** — the Terragrunt **live units** that wire the modules together for the `dev` environment, supply their inputs, and inherit the shared remote-state backend and provider/version config from `environments/dev/root.hcl`. Additional environments are added as sibling folders under `environments/`.

## Running a unit

Each live unit is run from its own folder with Terragrunt:

```powershell
cd infrastructure/environments/dev/<unit>
terragrunt init     # wire the backend, download providers, resolve dependencies
terragrunt plan     # safe, read-only preview of what would change
terragrunt apply    # make it real, after confirmation
```

`plan` before `apply`, every time. To act on every unit at once, `terragrunt run-all <command>` from `environments/dev` respects the dependency order below.

## Dependency ordering

Two bootstrap steps run **with the Azure CLI before any Terraform** (the identity and state backend must exist first — see Steps 1–2 of the Infrastructure Guide):

1. **Terraform service principal + RBAC** (Contributor + Role Based Access Control Administrator).
2. **Remote-state storage account + container** (the `azurerm` backend), then the Terragrunt root.

Then the units apply in dependency order (Terragrunt enforces this via `dependency` blocks):

- **`resource-group`** — first; almost everything else depends on it.
- **`networking`, `container-registry`, `key-vault`, `observability`, `cosmosdb`, `postgresql`, `redis`, `servicebus`, `eventgrid`, `communication-services`** — each depends on `resource-group`.
- **`function-app`** — depends on `resource-group` **and** `observability` (for the App Insights connection string).
- **`role-assignments`** — depends on `function-app`, `key-vault`, `servicebus`, and `eventgrid` (grants the Function App's managed identity scoped data-plane roles).
- **`aks`** — depends on `resource-group`, `networking` (the `aks` subnet), `container-registry` (to scope the kubelet AcrPull grant), and `observability` (the Log Analytics workspace for the OMS agent).
- **`workload-identity`** — depends on `resource-group`, `aks` (the OIDC issuer URL the federated credentials trust), `key-vault`, `servicebus`, and `eventgrid` (the scopes for each service identity's least-privilege roles).
- **`github-oidc`** — depends on `resource-group` and `container-registry` (to scope the AcrPush grant). Provisions the CI/CD user-assigned identity `id-ak-cicd-dev` with GitHub federated credentials (issuer `token.actions.githubusercontent.com`, subjects for `ref:refs/heads/master` and `environment:dev`) and **AcrPush only** — CD authenticates via OIDC with no stored secret and cannot touch the cluster. See [ADR-023](../docs/adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](../docs/guides/devops-cicd-guide.md).
- **`app-registration`** — independent of the resource group (a directory object, not a resource-group resource).
- **`governance`** — depends on `resource-group` (a budget scoped to it).

**Networking NSGs and internet ingress.** The `networking` module builds one Network Security Group per subnet with a deny-by-default inbound baseline. Because the NSG is customer-managed on a bring-your-own VNet, AKS does **not** automatically add LoadBalancer rules for a public Service — so each subnet carries a per-subnet **`allow_internet_ingress`** flag (default `false`). When set, that subnet's NSG additionally allows inbound **80/443 from the `Internet` service tag**; the dev environment sets it on the **`aks`** subnet only (which hosts the ingress controller) and leaves the others closed. See the [AKS Guide ingress troubleshooting](../docs/guides/aks-guide.md#ingress-and-tls).

## Authentication prerequisites

- **Run as the Terraform service principal.** Set the four `ARM_*` environment variables before running any unit, so Terraform (and the state backend) authenticate as the automation identity:
  ```powershell
  $env:ARM_CLIENT_ID       = "<appId>"
  $env:ARM_CLIENT_SECRET   = "<password>"
  $env:ARM_TENANT_ID       = "<tenant>"
  $env:ARM_SUBSCRIPTION_ID = "<subscription-id>"
  ```
  The same identity needs **Storage Blob Data Contributor** on the state storage account (data-plane access to read/write state) — control-plane Contributor alone is not enough.
- **Directory-plane note for `app-registration`.** That unit uses the **`azuread`** provider (Microsoft Graph / the directory), not the resource manager. The service principal's subscription roles do **not** grant directory permissions — if its apply fails with an insufficient-privileges / Graph error, grant the SP the **Application Administrator** Entra role (or `Application.ReadWrite.All`), or run that one unit as a signed-in admin user.

## Status

The **dev environment foundation is complete** — all units above are implemented as reusable modules with matching Terragrunt live configuration, a shared remote-state backend, a shared provider-version pin, least-privilege identity-based access, and a cost budget with alerts.

For the concepts behind each resource and the per-resource verification commands, see the [Infrastructure Guide](../docs/guides/infrastructure-guide.md); for the reasoning behind the platform choices, see the [Architecture Decision Records](../docs/adr/README.md).
