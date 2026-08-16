# Infrastructure Guide — Terraform & Terragrunt

## Purpose

This guide explains how the platform's cloud infrastructure is provisioned with **Terraform** and **Terragrunt** — the concepts behind each resource, the scripts that define it (explained in detail), the commands that execute it, and the checks that verify it exists and is configured correctly.

It is written to serve two audiences at once: as a **build reference** for provisioning the environment, and as a **learning resource** for understanding what each resource is and why it is configured the way it is. Every infrastructure component is documented to the same depth and in the same shape, so the guide reads consistently from start to finish.

---

## How to Use This Guide

Every infrastructure component below follows the same **four-part rhythm**:

1. **Understand** — the concept. What the resource is, the problem it solves, and how it fits into the wider architecture.
2. **Build** — how the Terraform/Terragrunt is written. The configuration is shown with heavy inline comments and explained block by block, so the *why* behind each setting is clear, not just the *what*.
3. **Execute** — the commands that provision it (`terraform` / `terragrunt` plan and apply), including the order of operations and any dependencies on earlier components.
4. **Verify** — the checks that confirm the resource exists and is correct, using the Azure CLI and the portal, with the expected output described.

Read a section top to bottom to learn a resource end to end; jump straight to **Execute** and **Verify** when you are provisioning or validating.

---

## Prerequisites & Authentication Model

Before provisioning anything, it is important to understand *who* Terraform acts as and *what* it is allowed to do.

- **Identity tenant vs. subscription.** Identities (users, groups, service principals, app registrations) live in the **identity tenant**; billable resources live in a **subscription**. The two are distinct: the tenant answers "who are you", the subscription answers "where do resources live and who pays". Terraform needs context in both.
- **The service principal Terraform authenticates as.** Terraform does not run as a human. It authenticates as a dedicated **service principal** — a non-interactive identity with its own credentials — so that automation is repeatable and auditable, and so the same configuration runs identically on a developer machine and in a pipeline.
- **Authorization via RBAC roles.** Authenticating proves *identity*; it does not grant *permission*. The service principal is assigned **role-based access control (RBAC)** roles at the subscription scope. The automation identity holds **exactly two roles** — **Contributor** to create and manage resources, and **Role Based Access Control Administrator** to manage the role assignments the platform provisions for its own managed identities — and nothing wider (least privilege). The rationale is in Step 1.
- **Remote state with locking.** Terraform records what it has created in **state**. State is stored **remotely** (not on a single machine) so the team shares one source of truth, and it is protected by **locking** so two runs cannot modify the same infrastructure concurrently and corrupt it.

> The Terraform service principal and its RBAC assignment are created in **Step 1** below — with the Azure CLI, because the automation identity must exist before Terraform can authenticate as it. The remote state backend and Terragrunt root are bootstrapped in **Step 2**.

---

## Repository Hygiene

Terraform and Terragrunt generate local working files — the `.terraform/` provider directory, the `.terragrunt-cache/`, and `*.tfstate` files — that are transient or environment-specific and are **gitignored**; they are never committed. The one deliberate exception is the **provider lock file** (`.terraform.lock.hcl`), which **is committed**: it pins the exact provider versions so every run — on any machine or in a pipeline — resolves the same providers, the infrastructure equivalent of pinned package versions and the basis for reproducible builds. State itself lives only in the remote Azure backend (Step 2), never in the repository.

---

## Diagnostics & Day-to-Day Commands

Reference material for working with — and troubleshooting — Terraform/Terragrunt with confidence. Read it once; return to it whenever a plan or apply looks off.

### The commands you use every day

| Command | What it does | When to use it |
|---------|--------------|----------------|
| `terragrunt init` | Wires up the backend and downloads the providers for a unit. | On first use of a unit, and after backend or module/source changes. |
| `terragrunt plan` | A **safe, read-only** comparison of configuration vs. state vs. real Azure. Changes nothing. | Anytime — and **always before `apply`**. |
| `terragrunt apply` | Executes the plan, **after explicit confirmation**, creating/updating/deleting real resources. | When the plan shows exactly what you intend. |
| `terragrunt output` | Shows a unit's outputs; sensitive ones are masked. | To read a unit's results. Use `terragrunt output -raw <name>` to reveal a sensitive value **deliberately, only when needed**. |
| `terragrunt state list` | Lists what Terraform currently tracks — its "memory". | To see which resources state believes exist. |
| `terragrunt untaint <resource>` | Clears a resource's "must be replaced" mark, once you have verified it is actually healthy. | After confirming a resource flagged for replacement is genuinely fine. |
| `az <service> show ...` | Verifies the **real** resource in Azure, independent of Terraform. | To check ground truth — what actually exists, regardless of state. |

> **Read `plan` correctly.** `plan` is a health check, not an action. The message **`No changes. Your infrastructure matches the configuration.`** means **configuration, state, and real Azure all agree** — that is the confirmation of health you are looking for, *not* an "empty" or failed result.

### When something unexpected happens: the diagnostic workflow

When a plan or apply surprises you, work through this calm, ordered playbook — don't reach for destroy/recreate.

1. **Check reality first.** Does the resource actually exist, and is it healthy? Azure is the ground truth:
   ```powershell
   az <service> show --resource-group <rg> --name <name> --query "{name:name, state:provisioningState}" -o json
   ```
2. **Check Terraform's memory.** Does state contain what you expect?
   ```powershell
   terragrunt state list
   ```
3. **Read the plan for the *why*, not just the summary.** The cause is in the diff, not the "N to add/change/destroy" line:
   - **`# ... is tainted, so must be replaced`** — a previous `apply` failed mid-way and marked the resource for replacement.
   - **an attribute marked `# forces replacement`** — configuration and reality have drifted; the **marked attribute names the exact cause**.
4. **Fix the configuration to describe full reality** (or `untaint` if the resource is verified healthy). **Never delete/recreate reflexively** — that risks data and is rarely the right fix.
5. **Confirm with a re-plan.** `No changes.` is the proof the fix worked — achieved **without touching any resource**.

### A real scenario: provider drift

Cloud platforms sometimes add **server-side defaults the configuration never declared**. For example, a Cosmos DB MongoDB-kind account implicitly receives an `EnableMongo` capability from Azure, even though the configuration only declared `EnableServerless`.

What happens on the next `plan`:

- Terraform sees a capability in reality that is not in the configuration, so it plans to **"remove"** it.
- For Cosmos capabilities, removing one **forces a destroy-and-recreate** of the whole account — and `lifecycle.prevent_destroy` on that stateful resource **correctly blocks it**, so the apply fails loudly rather than destroying data.

**The remedy** is not to fight the platform: **declare the platform-added setting in the configuration** so config matches reality (here, add a second `capabilities { name = "EnableMongo" }` block). The re-plan then reports **`No changes.`**

Two lasting lessons:

- **(a) Put `prevent_destroy` on stateful resources *before* you need it.** It is what turned a silent, destructive replacement into a safe, blocking error.
- **(b) Read plan diffs line by line.** The `# forces replacement` marker named the exact culprit, turning a confusing failure into a one-line fix.

### Common platform errors and how to read them

Most provisioning errors become clear **once you read the error body, not just the status code**. The status (401/403/500) gives a category; the **message text** gives the actual cause and the fix.

| Symptom | What it really means | Fix |
|---------|----------------------|-----|
| **A secret was accidentally exposed** (committed, logged, printed) | The credential is compromised the moment it leaves a trusted boundary | **Rotate it immediately** (e.g. `az ad sp credential reset`, regenerate the key), then remove it from wherever it leaked — a rotated secret makes the exposed one useless. |
| **Provider drift** — plan wants to "remove" a setting you never declared | The cloud added a **server-side default** the config doesn't mention, so Terraform plans to take it away (for some settings, a destroy-and-recreate) | **Declare the platform-added setting** so config matches reality; the re-plan then says `No changes`. (See the provider-drift scenario above.) |
| **Quota wall** — a `401` / `403` whose **body says "additional quota required"** | Not an auth problem despite the status code — the subscription has **no remaining capacity** for that SKU/region | **Request the quota** for the **specific SKU and region** (portal → Quotas, or a support request). Auth is fine; capacity is the blocker. |
| **Insufficient privileges on a directory object** — `403 Authorization_RequestDenied` | The identity has **resource-plane** rights (subscription RBAC) but **not directory-plane** rights (Entra) — e.g. creating an app registration | **Run that unit as an admin user**, or grant the identity the **directory role** it needs (Application Administrator / `Application.ReadWrite.All`). |
| **State-lock authorization failure** — `403 AuthorizationPermissionMismatch` while "Initializing the backend" | The identity can authenticate but **lacks data-plane access to the state storage** | Grant it **Storage Blob Data Contributor** on the **state storage account** (control-plane Contributor does not include blob data access), or run as the identity that has it. |
| **Identifier-URI policy rejection** — the tenant refuses a friendly `api://<name>` URI | The app is issuing **v1 access tokens**, under which the tenant's identifier-URI policy rejects the friendly form | Set **`requested_access_token_version = 2`** on the app's `api` block — v2 permits the friendly identifier URI (and emits cleaner claims). |

> **The meta-lesson: read the error *body*, not just the status code.** A `403` can mean "you lack a role", "you lack a directory permission", "you lack data-plane access", or even "you're out of quota" — and the **message text** is what tells them apart. The status narrows the category; the body names the cause and points at the fix.

---

## Infrastructure Components

Each component below is documented with the same four-part structure. Sections are filled in as the component is built, capturing the real configuration, the real commands, and the real verification output.

### 1. Terraform Identity & Access (Service Principal, RBAC)

> This step uses the **Azure CLI, not Terraform** — the automation identity must exist before Terraform can authenticate as it, so there is no `.tf` script here, only documented commands.
>
> Background reading: [Identity Concepts](identity-concepts.md) — tenant vs subscription, authentication vs authorization, RBAC roles and scopes, and service principals vs managed identities.

#### Understand

**Tenant (directory) vs. subscription.** A **tenant** is the *identity* boundary — it holds users, groups, service principals, and app registrations. A **subscription** is the *billing and resource* boundary — it holds and pays for the resources you create. One tenant can contain **many** subscriptions; each subscription belongs to **exactly one** tenant. A single user can belong to **many** tenants. In short: the tenant answers "who are you", the subscription answers "where do resources live and who pays".

**Authentication vs. authorization.** These are two separate things:
- **Authentication** proves *who* an identity is. A **service principal** is a non-human identity (the automation equivalent of a user account) used to authenticate with no person present.
- **Authorization** decides *what* that identity may do. It is granted by an **RBAC role assignment**, which binds a role (a set of permissions) to an identity at a scope. Authenticating successfully grants nothing on its own — a role assignment is what confers permission.

**Why a service principal instead of a personal login.**
- **Non-interactive** — automation cannot answer an MFA prompt; a service principal authenticates with credentials, no human interaction.
- **Least privilege & bounded blast radius** — it carries only the permissions it needs, and if its secret leaks you rotate one secret rather than exposing a person's full access.
- **Auditability** — automated actions are attributable to the automation identity, kept distinct from human activity in the logs.
- **Clean lifecycle** — it can be created, scoped, rotated, and deleted independently of any individual's account.

**Why these two roles — and only these two.** The automation identity holds exactly two roles, each for a distinct, deliberate reason:
- **Contributor** — to create, read, update, and delete resources (everything an IaC identity needs to build the environment). Contributor deliberately **cannot grant access to others** — it cannot create role assignments — which is precisely why a second role is required.
- **Role Based Access Control Administrator** — to create and manage **role assignments**. The platform provisions its own RBAC grants as part of the infrastructure: managed identities are granted access to the Key Vault, the messaging namespace, and the container registry. Building those grants requires an identity that can manage role assignments.

This is a **least-privilege design choice**. The ability to manage role assignments could also come from the broader **User Access Administrator** role, but **Role Based Access Control Administrator** is the narrower, purpose-built role for exactly that task. Both can manage role assignments; choosing the narrower one gives the identity precisely the two capabilities the platform needs — **resource management** and **RBAC management** — and nothing wider. The assignment required no additional condition.

**How Terraform consumes this.** The `azurerm` provider authenticates as the service principal using four environment variables — `ARM_CLIENT_ID` (the SP's application id), `ARM_CLIENT_SECRET` (its secret), `ARM_TENANT_ID` (the directory), and `ARM_SUBSCRIPTION_ID` (where resources are created). When these are present, Terraform runs as the service principal with its assigned permissions, with no `az login` and no interactive sign-in.

#### Build

This step has **no Terraform script**. There is a bootstrapping order: you cannot use Terraform to create the very identity Terraform signs in with, so the service principal and its two role assignments are created once, up front, with the **Azure CLI**.

The only outputs that matter for later steps are the four `ARM_*` values. The **client secret is sensitive** — it is never written to a `.tf` file, a variables file, or anything committed to the repository. It is supplied to Terraform only through environment variables (and later through a secret store or pipeline secret).

#### Execute

Run from PowerShell, signed in to the correct tenant (`az login`) with the target subscription selected. Replace `<subscription-id>` (and the captured `<appId>` / `<password>` / `<tenant>`) with real values.

```powershell
# 0. Select the target subscription (the resource home and the role-assignment scope)
az account set --subscription "<subscription-id>"

# 1. Create the service principal with Contributor scoped to the subscription.
#    This prints appId, password, and tenant exactly once — capture them now.
az ad sp create-for-rbac `
  --name "antkart-terraform-sp" `
  --role "Contributor" `
  --scopes "/subscriptions/<subscription-id>"

# 2. Assign the second role so the identity can manage the RBAC grants the
#    platform provisions (e.g. managed identities -> Key Vault, Service Bus,
#    container registry). No additional condition is required.
az role assignment create `
  --assignee "<appId>" `
  --role "Role Based Access Control Administrator" `
  --scope "/subscriptions/<subscription-id>"

# 3. Set the four ARM_* environment variables for this session so Terraform
#    authenticates as the service principal. Use the values printed in step 1.
$env:ARM_CLIENT_ID       = "<appId>"
$env:ARM_CLIENT_SECRET   = "<password>"
$env:ARM_TENANT_ID       = "<tenant>"
$env:ARM_SUBSCRIPTION_ID = "<subscription-id>"
```

What each does:
- **Step 0** — sets the active subscription so the service principal and its role assignments land in the right place.
- **Step 1** — creates the `antkart-terraform-sp` service principal and assigns it **Contributor** at subscription scope; prints the credentials once.
- **Step 2** — assigns **Role Based Access Control Administrator** at subscription scope so the identity can create and manage role assignments (with no additional condition).
- **Step 3** — exports the four `ARM_*` variables so the `azurerm` provider authenticates as the service principal for the rest of the session.

`az ad sp create-for-rbac` prints the password only once. If it is lost, reset it (see the security note) rather than recreating the service principal.

#### Verify

```powershell
# Confirm both role assignments at the subscription scope.
az role assignment list --assignee "<appId>" -o table
```

A **correct result** shows **two rows**, both with **Principal** = the SP's `appId` and **Scope** = `/subscriptions/<subscription-id>`: one with **Role** = `Contributor` and one with **Role** = `Role Based Access Control Administrator`.

In the portal:
- **Microsoft Entra ID → App registrations** → search `antkart-terraform-sp` to see the identity.
- **Subscription → Access control (IAM) → Role assignments** → filter by the SP name → it appears with both the **Contributor** and **Role Based Access Control Administrator** roles at subscription scope.

Optional end-to-end check once the variables are set: a later Terraform `plan` that authenticates without prompting confirms the credentials and permissions are wired correctly.

> **Security note.** The client secret (`ARM_CLIENT_SECRET`) is a credential equivalent to a password. Store it only in environment variables now, and in a secret store or pipeline secret later — **never** in the repository, a `.tf` file, or a committed variables file. Secrets can and should be rotated: `az ad sp credential reset --id <appId>` issues a new secret and invalidates the old one, without recreating the service principal or its role assignment.

### 2. Remote State Backend & Terragrunt Root

> This step bootstraps the state backend with the **Azure CLI** (the storage must exist before Terraform can write state to it) and adds the first **Terragrunt** configuration under `infrastructure/`.

#### Understand

**What Terraform state is.** Terraform keeps a **state file** — the source-of-truth mapping between the resources declared in code and the real resources that exist in Azure. It records each resource's identity and attributes so Terraform knows what already exists, what to change, and what to destroy. Without state, Terraform cannot tell "create this" apart from "this already exists".

**Why state must be remote.** By default, state is a local file, which is fragile: it lives on one machine, is easily lost, and cannot be shared. Storing it **remotely** (in Azure Storage) makes it **shareable** across developer machines and pipelines — everyone reads and writes one authoritative copy — and **durable**, protected by the storage account's redundancy rather than a single laptop.

**Why state must be locked.** If two `apply` operations run against the same state at once, they can interleave writes and corrupt it. The backend therefore **locks** the state for the duration of an operation, **serializing** applies so only one runs at a time; a second is refused until the first releases the lock. Locking — together with a single shared remote state — also prevents **configuration drift**: there is one source of truth that everyone plans and applies against, so the code and the real infrastructure stay aligned.

**The Terragrunt root (`root.hcl`).** Rather than repeat the backend configuration in every module, the platform configures it **once** in a Terragrunt root file. Every module includes this root, so they all inherit the same backend — the setup is **DRY**, and because it lives in exactly one place it cannot drift between modules. The root also generates a shared provider so every module authenticates the same way.

#### Build

This step adds two things under `infrastructure/`:

- **`infrastructure/environments/dev/root.hcl`** — the Terragrunt root. Its `remote_state` block declares the **`azurerm`** backend and points it at a dedicated state **resource group**, **storage account**, and blob **container**. The `generate` directive writes a `backend.tf` into each child module at init time, so every module inherits the backend without copy-paste. The `key` is derived per module (`${path_relative_to_include()}/terraform.tfstate`), giving each module its own isolated state blob so two modules can never write to the same state. `use_azuread_auth = true` means the backend authenticates to the state storage with the caller's **Azure AD (Entra) identity** — the Step 1 service principal — **rather than a storage account access key**. This keeps the state backend consistent with the platform's secret-less, Entra-based security model and avoids storage account keys entirely: the storage account has **shared-key access disabled** (so Azure AD is the only supported path), and the service principal is granted the **Storage Blob Data Contributor** role on the state account to read and write the state blobs — **no storage key and no secret in the repo**. A `generate "provider"` block writes a shared `azurerm` provider into each module, reading the `ARM_*` variables to authenticate. State **locking** is built into the `azurerm` backend via a **blob lease**, and the storage account encrypts state **at rest** by default.
- **`infrastructure/modules/`** — established now (empty) as the home for the reusable resource modules added from Step 3 onward.

The `root.hcl` is heavily commented and explained block by block in the file itself; the key blocks are `locals` (the backend coordinates), `remote_state` (backend + `generate "backend.tf"` + per-module `key` + Entra auth), `generate "provider"` (the shared provider), and `inputs` (environment-wide values).

#### Execute

State backends are bootstrapped with the **Azure CLI**, because the storage account must exist before Terraform/Terragrunt can write state to it — the same bootstrapping order as the Step 1 identity. Run from PowerShell, signed in with `az login` (and `ARM_*` set from Step 1).

**(a) Create the state backend — resource group, storage account, data-plane role, container:**

```powershell
# Names — keep the storage account name globally unique, lowercase, 3-24 chars
$STATE_RG        = "rg-antkart-tfstate"
$STATE_SA        = "stantkarttfstate"   # change if this name is already taken
$STATE_CONTAINER = "tfstate"
$LOCATION        = "eastus"

# 1. Dedicated resource group for Terraform state (kept apart from app resources)
az group create --name $STATE_RG --location $LOCATION

# 2. Storage account: locally redundant, TLS 1.2+, no public blob access, and
#    shared-key access disabled so ALL access is via Entra identity (no keys).
az storage account create `
  --name $STATE_SA `
  --resource-group $STATE_RG `
  --location $LOCATION `
  --sku Standard_LRS `
  --kind StorageV2 `
  --min-tls-version TLS1_2 `
  --allow-blob-public-access false `
  --allow-shared-key-access false

# 3. Grant the Terraform service principal DATA-plane access to the state.
#    Control-plane Contributor does NOT include blob data access, so the
#    identity that reads/writes state needs Storage Blob Data Contributor.
$SA_ID = az storage account show --name $STATE_SA --resource-group $STATE_RG --query id -o tsv
az role assignment create `
  --assignee "<terraform-sp-appId>" `
  --role "Storage Blob Data Contributor" `
  --scope $SA_ID

# 4. Blob container that will hold the state files (created via Entra auth)
az storage container create `
  --name $STATE_CONTAINER `
  --account-name $STATE_SA `
  --auth-mode login
```

**(b) Initialize Terragrunt.** `root.hcl` is included by child modules; it is not applied on its own. Terragrunt initializes the backend the first time a module that includes it is run — the **Resource Group** in Step 3. On that first init, Terragrunt generates `backend.tf` and `provider.tf` in the module and connects it to the remote state:

```powershell
# Run from a module directory that includes the root (the first is Step 3).
cd infrastructure/environments/dev/<module>
terragrunt init     # wires up the backend; the first apply then creates the state blob
```

> From `infrastructure/environments/dev`, `terragrunt run-all init` initializes every module at once, once modules exist.

What each does:
- **`az group create`** — creates the dedicated resource group that holds the state storage account.
- **`az storage account create`** — creates the state storage account with key access disabled (Entra-only), TLS 1.2+, and no public blob access.
- **`az role assignment create` (Storage Blob Data Contributor)** — grants the Terraform identity data-plane access so it can read/write state blobs (Contributor alone cannot).
- **`az storage container create`** — creates the `tfstate` container that holds the per-module state blobs.
- **`terragrunt init`** — generates the backend/provider into a module and connects it to the remote state (run from Step 3 onward).

#### Verify

```powershell
# State storage account exists and is provisioned
az storage account show --name $STATE_SA --resource-group $STATE_RG -o table

# State container exists (Entra-auth data-plane call)
az storage container show --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table
```

A **correct result** shows the storage account with `provisioningState = Succeeded` and `kind = StorageV2`, and the container reported as present.

Confirm the account is hardened for Azure AD-only access:

```powershell
az storage account show --name $STATE_SA --resource-group $STATE_RG `
  --query "{minTls:minimumTlsVersion, sharedKey:allowSharedKeyAccess}" -o table
```

A **correct result** shows `minimumTlsVersion = TLS1_2` and `allowSharedKeyAccess = False` — confirming the backend is reachable only over TLS 1.2+ and only via Azure AD (no storage account keys).

**State locking** is active automatically: while a `terragrunt apply` runs, the backend holds a **lease** on the state blob, and a second concurrent apply is refused until it is released. You can observe the lease during an apply:

```powershell
az storage blob show `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --name "<module>/terraform.tfstate" --auth-mode login `
  --query "properties.lease" -o json
# during an apply: leaseState = "leased"
```

**The state blob appears after the first apply.** Before Step 3 the container is empty; after the first module's `terragrunt apply`, a state blob appears at the module's key:

```powershell
az storage blob list `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --auth-mode login -o table
# e.g. resource-group/terraform.tfstate
```

### 3. Resource Group

> This step adds the **first Terraform module** (`modules/resource-group`) and the **first Terragrunt live unit** (`environments/dev/resource-group`) — the first thing actually provisioned.

#### Understand

**What an Azure Resource Group is.** A **resource group** is a logical container for related Azure resources. It is the unit of **lifecycle** (resources in a group are typically created and deleted together), **access control** (RBAC can be scoped to the group), **tagging**, and **cost grouping** (costs roll up by group). Almost every resource lives in exactly one resource group, so it is the natural first thing to create — the parent scope everything else is placed in.

**The module-vs-live split.** The platform separates *how* from *what*:
- The **reusable module** (`infrastructure/modules/resource-group`) defines **how** a resource group is built — its inputs, the resource, and its outputs — with **no hardcoded environment values**.
- The **environment (live) config** (`infrastructure/environments/dev/resource-group`) supplies **what** — this environment's name, location, and tags — and **reuses** the module via Terragrunt.

This keeps the setup **DRY** (the resource is defined once) and ensures environments are **structurally identical** — dev, and any future environment, run the same module and differ only in their inputs. Every component from here on follows this split.

**`prevent_destroy`.** A resource group is a **stateful container** that holds every resource in the environment, so the module sets `lifecycle { prevent_destroy = true }` on it: Terraform will refuse any plan that would delete the resource group until the guard is intentionally removed, protecting against an accidental teardown that would take everything with it.

#### Build

This step adds the first **module** and its first **live unit**:

- **`infrastructure/modules/resource-group/`** — the reusable module:
  - **`variables.tf`** — the inputs: `name`, `location`, and `tags`, each with a description. `name` and `location` are required (no defaults); `tags` defaults to an empty map. No environment values are baked in.
  - **`main.tf`** — a single `azurerm_resource_group` built entirely from those inputs, with `lifecycle { prevent_destroy = true }`.
  - **`outputs.tf`** — exports `name`, `id`, and `location`, which later modules consume as their parent scope.
- **`infrastructure/environments/dev/resource-group/terragrunt.hcl`** — the live unit:
  - `include "root"` pulls in `root.hcl` (the shared Azure AD backend + provider).
  - `terraform { source = "../../../modules/resource-group" }` points at the module.
  - `inputs { ... }` supplies dev's values: `name = "rg-antkart-dev-eastus"`, `location = "eastus"`, and `tags` (`environment`, `project`, `managed-by`).

All files are heavily commented and explained inline.

#### Execute

> **Heads-up — a resource group named `rg-antkart-dev-eastus` already exists** (created manually in earlier work). Terraform must **create and own** what it manages, so before applying you must resolve this so Terraform is not fighting a resource it did not create. Choose one:
>
> - **(a) Recommended — delete the pre-existing RG for a clean start.** It is empty, so removing it lets Terraform create and own it cleanly. Confirm it is empty first, then delete:
>   ```powershell
>   # Confirm the group is empty (expect: an empty list / no rows) BEFORE deleting
>   az resource list --resource-group "rg-antkart-dev-eastus" -o table
>   # Delete it — only after confirming it is empty
>   az group delete --name "rg-antkart-dev-eastus" --yes
>   ```
> - **(b) Alternative — import it into Terraform state** instead of deleting (use this only if it ever holds resources worth keeping):
>   ```powershell
>   terragrunt import azurerm_resource_group.this `
>     "/subscriptions/<subscription-id>/resourceGroups/rg-antkart-dev-eastus"
>   ```
>
> Decide before `apply`. With the empty group, option (a) is the clean choice. **Do not run anything until you have decided.**

Run from the live unit's folder:

```powershell
cd infrastructure/environments/dev/resource-group

# 1. First init — wires up the Azure AD state backend, generates backend.tf and
#    provider.tf from the root, downloads the provider, and on this first run
#    creates the state blob for this unit in the tfstate container.
terragrunt init

# 2. Plan — review what will change before applying.
terragrunt plan

# 3. Apply — create the resource group. Confirm when prompted.
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates `backend.tf`/`provider.tf` from the root, connects to the Azure AD remote state, and initializes the provider.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add, 0 to change, 0 to destroy** (the resource group).
- **`terragrunt apply`** — creates the resource group and writes this unit's state to the backend.

#### Verify

```powershell
# 1. The resource group exists with the expected location and tags
az group show --name "rg-antkart-dev-eastus" -o table

# 2. The state blob now appears in the tfstate container (Azure AD auth)
az storage blob list `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --auth-mode login -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az group show` reports the group with `Location = eastus`, `ProvisioningState = Succeeded`, and the tags applied.
- `az storage blob list` now shows a blob at **`resource-group/terraform.tfstate`** — confirming the remote state, locking, and Azure AD auth from Step 2 work end to end.

### 4. Networking (VNet, Subnets, NSGs)

> Background reading: [Networking & Kubernetes Concepts](networking-concepts.md) — IP addressing & CIDR, VNet/subnet/NSG, Kubernetes fundamentals, and the Azure CNI IP-sizing math behind how these subnets are sized.

#### Understand

A quick recap (full detail in the [Networking & Kubernetes Concepts](networking-concepts.md) primer): a **VNet** is the private address space; **subnets** are non-overlapping per-workload slices of it; **NSGs** are stateful firewalls attached to subnets.

This step provisions a VNet and three subnets, each with its own NSG:

| Network | Range | Addresses | Why this size |
|---------|-------|-----------|---------------|
| **VNet** | `10.0.0.0/16` | 65,536 | The private space every subnet is carved from, sized large so there is room to grow. |
| **AKS subnet** | `10.0.0.0/22` | 1,024 | The **large** one: with traditional Azure CNI, **each pod consumes a subnet IP and each node pre-reserves a block of them**, so the cluster needs hundreds of addresses plus upgrade/scale headroom. |
| **Private-endpoints subnet** | `10.0.4.0/24` | 256 | Holds private endpoints to managed services; each consumes one IP, so a few hundred is ample. |
| **Gateway / APIM subnet** | `10.0.5.0/27` | 32 | A small, dedicated slice for the edge/gateway. |

Every subnet range sits **inside the VNet's `/16`** and the ranges **must not overlap**: `10.0.0.0/22` covers the third octet `.0`–`.3`, `10.0.4.0/24` is `.4`, and `10.0.5.0/27` sits in `.5`. Each subnet gets its **own NSG** so traffic to the cluster, the endpoints, and the gateway is controlled independently.

#### Build

This step adds a reusable **networking module** and its **dev live unit**:

- **`infrastructure/modules/networking/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `vnet_name`, `vnet_address_space`, a structured `subnets` map (each entry: `address_prefixes`, plus optional `service_endpoints` and `private_endpoint_network_policies`), and `tags`. No environment values baked in.
  - **`main.tf`** — an `azurerm_virtual_network`; the subnets via `azurerm_subnet` using **`for_each` over the `subnets` map** (so adding a subnet is a data change, not new code); one `azurerm_network_security_group` per subnet with an explicit **deny-by-default baseline** (allow intra-VNet, allow Azure Load Balancer probes, deny all other inbound — NSGs are stateful, so return traffic needs no separate rule); and `azurerm_subnet_network_security_group_association` to attach each NSG to its subnet.
  - **`outputs.tf`** — `vnet_id`, `vnet_name`, and `subnet_ids` (a map of subnet name → id) that later modules (AKS, private endpoints, gateway) consume.
- **`infrastructure/environments/dev/networking/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** block pointing at the `../resource-group` unit; it consumes that unit's `name` and `location` **outputs** rather than hardcoding them, and Terragrunt uses the dependency to apply the resource group **first**.
  - `terraform { source = "../../../modules/networking" }`.
  - `inputs` with dev's values: VNet `10.0.0.0/16`; subnets `aks = 10.0.0.0/22`, `private-endpoints = 10.0.4.0/24` (with `private_endpoint_network_policies = "Disabled"`), `gateway = 10.0.5.0/27`; matching tags.

This module **depends on the Resource Group** from Step 3, but only through the **Terragrunt dependency / outputs** mechanism — never hardcoded values — so the wiring stays correct if the resource group changes.

#### Execute

Run from the networking unit's folder:

```powershell
cd infrastructure/environments/dev/networking

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the VNet, subnets, NSGs, and associations
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports the **VNet + 3 subnets + 3 NSGs + 3 NSG-subnet associations** being **added** (≈ 10 to add), **0 to destroy**.
- **`terragrunt apply`** — creates the resources and writes this unit's state to the backend.

> The resource group must already exist (Step 3). Terragrunt applies the dependency first; if it has not been applied yet, apply the `resource-group` unit before this one (or run `terragrunt run-all apply` from `environments/dev`).

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# VNet and its address space (expect 10.0.0.0/16)
az network vnet show --resource-group $RG --name "vnet-antkart-dev-eastus" `
  --query "{name:name, addressSpace:addressSpace.addressPrefixes}" -o json

# Subnets and their prefixes
az network vnet subnet list --resource-group $RG --vnet-name "vnet-antkart-dev-eastus" `
  --query "[].{name:name, prefix:addressPrefix}" -o table

# NSGs (expect one per subnet)
az network nsg list --resource-group $RG --query "[].name" -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: N added, 0 changed, 0 destroyed.` (N = the VNet + 3 subnets + 3 NSGs + 3 associations).
- `az network vnet show` reports the address space `10.0.0.0/16`.
- `az network vnet subnet list` shows `aks` → `10.0.0.0/22`, `private-endpoints` → `10.0.4.0/24`, `gateway` → `10.0.5.0/27`.
- `az network nsg list` shows `nsg-aks`, `nsg-private-endpoints`, and `nsg-gateway`, each associated to its subnet.

### 5. Azure Container Registry

#### Understand

A **container registry** is a private repository for **container images** — the packaged, ready-to-run artifacts the platform's services are built into. Images are pushed to the registry and later pulled by whatever runs them (the Kubernetes cluster, in a later step).

The platform uses a **private Azure Container Registry (ACR)** rather than a public registry because:

- **Images stay inside the Azure tenant** — access is controlled through Azure AD (Microsoft Entra) identities and RBAC, not exposed publicly.
- **Proximity to the cluster** — pulls happen within Azure's network, so they are faster and more reliable than pulling across the public internet.
- **No public rate limits** — public registries throttle anonymous pulls; a private ACR does not.
- **Integrated authentication** — workloads authenticate with their Azure identity. The built-in **`AcrPull`** role grants pull access; AKS uses it in a later step.
- **Foundation for supply-chain security** — it is where image scanning and signing are layered on later.

**Tiers.** The **Basic** tier is used for dev. **Premium** (for production) adds **private endpoints**, **geo-replication**, and **content trust**.

> **Who pulls images?** When AKS is introduced, it is the cluster's **kubelet identity** — the identity the nodes use to pull images — that is granted `AcrPull`, **not** the cluster's control-plane identity. That role assignment is made with the AKS wiring, not in this step.

#### Build

This step adds a reusable **container-registry module** and its **dev live unit**:

- **`infrastructure/modules/container-registry/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `acr_name` (globally unique, alphanumeric, 5–50 chars), `sku` (default `"Basic"`), and `tags`. No environment values baked in.
  - **`main.tf`** — an `azurerm_container_registry` built from the inputs, with **`admin_enabled = false`**. Disabling the admin account removes the single static username/password and forces all access through Azure AD identities and RBAC — consistent with the platform's secret-less model. A comment notes that `AcrPull` for the AKS kubelet identity is granted in a later step, not here.
  - **`outputs.tf`** — `id`, `name`, and `login_server`, consumed by later steps (AKS scopes its `AcrPull` to the `id`; build/push uses the `login_server`).
- **`infrastructure/environments/dev/container-registry/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the networking unit's pattern).
  - `terraform { source = "../../../modules/container-registry" }`.
  - `inputs`: `acr_name = "acrantkartdev"`, `sku = "Basic"`, and matching tags.

Like the networking unit, this module **depends on the Resource Group** only through the Terragrunt dependency / outputs mechanism — never hardcoded.

#### Execute

Run from the ACR unit's folder:

```powershell
cd infrastructure/environments/dev/container-registry

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the container registry
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add** (the registry), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the registry and writes this unit's state to the backend.

> **The ACR name must be globally unique** (it becomes `<name>.azurecr.io`). If `apply` fails because the name is taken, change `acr_name` in the unit's `inputs` and re-run.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Registry name, SKU, login server, and that the admin account is disabled
az acr show --resource-group $RG --name "acrantkartdev" `
  --query "{name:name, sku:sku.name, loginServer:loginServer, adminEnabled:adminUserEnabled}" -o json
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az acr show` reports `name = acrantkartdev`, `sku = Basic`, `loginServer = acrantkartdev.azurecr.io`, and **`adminEnabled = false`** — confirming access is via Azure AD, not the admin account.

### 6. Key Vault

#### Understand

**Azure Key Vault** is a secure, managed store for **secrets** (connection strings, passwords, API keys), **keys** (cryptographic keys), and **certificates**. It is where sensitive values live so they never end up in the wrong place.

**Why secrets belong here, not in config / source / logs:**

- **Compliance** — secrets must never be committed to source control or baked into build artifacts; a vault keeps them out of both.
- **Separation of config from credentials** — application configuration can live with the code; the credentials it references stay in the vault.
- **Rotation without redeploying** — a secret can be updated in the vault and picked up at runtime, with no rebuild or redeploy of the app.
- **Least-privilege access** — only authorized identities can read a secret. Developers can read the code without ever seeing the secret values.
- **Audit logging** — every access to the vault is logged, so who read what and when is fully traceable.

**Access models — RBAC vs access policies.** Key Vault offers two ways to authorize access: the legacy **access policies** (a per-vault list managed separately from the rest of Azure) and **Azure RBAC** (the same role-based model used everywhere else). This platform uses **Azure RBAC** (`rbac_authorization_enabled = true`) so vault access is consistent with the platform's RBAC model and managed in one place.

**Soft delete & purge protection.** Soft delete is always on: when a vault is deleted it enters a **soft-deleted** state and its **name stays reserved for a retention period**, so the name cannot be reused immediately (you must recover or purge it first). **Purge protection**, when enabled, additionally prevents an early permanent purge — a soft-deleted vault must wait out the full retention window. Production enables purge protection; a disposable dev vault leaves it off for clean teardown.

> **Who reads secrets?** Application identities are **not** granted access here. The built-in **Key Vault Secrets User** role (read secrets at runtime) is assigned to the app/managed identities in the **managed-identity step**, not in this one.

#### Build

This step adds a reusable **key-vault module** and its **dev live unit**:

- **`infrastructure/modules/key-vault/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `key_vault_name` (globally unique, 3–24 chars, alphanumeric/hyphens), `tenant_id` (optional), `sku` (default `"standard"`), `soft_delete_retention_days` (default `7` for dev), `purge_protection_enabled` (default `false` for dev), and `tags`.
  - **`main.tf`** — an `azurerm_key_vault` with **`rbac_authorization_enabled = true`** (RBAC mode, not access policies), the SKU, and the soft-delete / purge settings. The **tenant id** comes from a `data.azurerm_client_config.current` data source (so the environment never hardcodes it), with an optional input override. A comment notes that the `Key Vault Secrets User` role for app identities is granted in a later step.
  - **`outputs.tf`** — `id` (to scope RBAC role assignments), `name`, and `vault_uri` (the endpoint apps call to read secrets).
- **`infrastructure/environments/dev/key-vault/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the other units).
  - `terraform { source = "../../../modules/key-vault" }`.
  - `inputs`: `key_vault_name = "kv-antkart-dev"`, `purge_protection_enabled = false`, matching tags. The **tenant id is not set** — the module resolves it from the current Azure context.

Like the other units, this module **depends on the Resource Group** only through the Terragrunt dependency / outputs mechanism.

#### Execute

Run from the Key Vault unit's folder:

```powershell
cd infrastructure/environments/dev/key-vault

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the Key Vault
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add** (the vault), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the vault and writes this unit's state to the backend.

> **Two name caveats:** (a) the vault name is **globally unique**; (b) if `kv-antkart-dev` was used before and soft-deleted, the name **may still be reserved** during the retention window — in that case recover/purge the soft-deleted vault, or pick a new name in the unit's `inputs`.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Name, RBAC authorization, purge protection, and soft-delete retention
az keyvault show --resource-group $RG --name "kv-antkart-dev" `
  --query "{name:name, rbacAuthorization:properties.enableRbacAuthorization, purgeProtection:properties.enablePurgeProtection, softDeleteDays:properties.softDeleteRetentionInDays}" -o json
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az keyvault show` reports `name = kv-antkart-dev`, `rbacAuthorization = true`, `purgeProtection = null/false` (dev), and `softDeleteDays = 7`.

### 7. Log Analytics & Application Insights

#### Understand

This step provisions the **observability foundation** — the destination where the platform's telemetry is stored and visualized. Three pieces work together, each with a distinct role:

- **OpenTelemetry** — the **instrumentation in the application code** that generates traces, metrics, and logs and propagates a **correlation ID** across calls. It is added later, in the code phase; it is the *source* of telemetry.
- **Application Insights** — the **APM (application performance monitoring) layer** that collects and visualizes application telemetry: requests, dependencies, exceptions, and **distributed traces**. It is where you see how the application behaves.
- **Log Analytics Workspace** — the **central store** that holds the telemetry, queried with **KQL** (Kusto Query Language).

**Why centralized telemetry is essential in microservices.** A single user request does not run in one process — it **fragments across many services and pods** (gateway → order → products → payments → notification, each possibly several replicas). Without a shared store and a **correlation ID** threading the logs and traces together, you cannot reassemble the request's full journey. The correlation ID lets you follow one request across every hop. This matters far more than in a monolith, where one request runs in a single process and its logs are naturally together.

**Why the workspace comes first.** Modern Application Insights is **workspace-based**: it stores its data **in a Log Analytics workspace** rather than in its own classic store. So the workspace is created first, and App Insights points at it (`workspace_id`), unifying application telemetry and platform logs in one queryable place.

> This step provisions the **destination** only. The application **instrumentation** (OpenTelemetry, correlation IDs) that produces the telemetry is wired later, in the code phase.

#### Build

This step adds a reusable **observability module** and its **dev live unit**:

- **`infrastructure/modules/observability/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `log_analytics_name`, `app_insights_name`, `retention_days` (default `30` — the workspace minimum, kept short for dev to keep cost low), and `tags`.
  - **`main.tf`** — an `azurerm_log_analytics_workspace` (`sku = "PerGB2018"`, `retention_in_days` from the variable) and an `azurerm_application_insights` with `application_type = "web"` and **`workspace_id`** pointing at the workspace (**workspace-based mode**). A comment notes that the application later uses this resource's **connection string** (and legacy instrumentation key) to send telemetry.
  - **`outputs.tf`** — `workspace_id`, `workspace_name`, `app_insights_id`, and the **`connection_string`** and **`instrumentation_key`** (both marked **`sensitive`**). App config consumes the connection string.
- **`infrastructure/environments/dev/observability/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the other units).
  - `terraform { source = "../../../modules/observability" }`.
  - `inputs`: `log_analytics_name = "log-antkart-dev"`, `app_insights_name = "appi-antkart-dev"`, `retention_days = 30`, matching tags.

Like the other units, this module **depends on the Resource Group** only through the Terragrunt dependency / outputs mechanism — never hardcoded.

#### Execute

Run from the observability unit's folder:

```powershell
cd infrastructure/environments/dev/observability

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the workspace and App Insights
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **2 to add** (the workspace and App Insights), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates both resources and writes this unit's state to the backend.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Log Analytics workspace (name, SKU, retention)
az monitor log-analytics workspace show --resource-group $RG --workspace-name "log-antkart-dev" `
  --query "{name:name, sku:sku.name, retentionInDays:retentionInDays}" -o json

# Application Insights (name, type, and that it is workspace-based)
az monitor app-insights component show --resource-group $RG --app "appi-antkart-dev" `
  --query "{name:name, appType:applicationType, workspace:workspaceResourceId}" -o json
```

> The `app-insights` commands require the Azure CLI **`application-insights`** extension. If prompted, install it with `az extension add --name application-insights` (or allow the auto-install) and re-run.

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 2 added, 0 changed, 0 destroyed.`
- `az monitor log-analytics workspace show` reports `name = log-antkart-dev`, `sku = PerGB2018`, `retentionInDays = 30`.
- `az monitor app-insights component show` reports `name = appi-antkart-dev`, `appType = web`, and a non-empty `workspace` (the workspace's resource id) — confirming it is **workspace-based**.

### 8. Cosmos DB

> Background reading: [Azure Cosmos DB Concepts](cosmosdb-concepts.md) — the resource hierarchy, the multi-API (MongoDB) model, Request Units, provisioned vs serverless, throttling (429), and partition keys.

#### Understand

This step provisions a fully-managed document database for the **product catalog**. The concepts are covered in the [Cosmos DB Concepts primer](cosmosdb-concepts.md); the decisions that shape this module:

- **Request Units (RUs)** are Cosmos's single currency of work — every read, write, and query consumes RUs.
- **Serverless** is chosen because the dev workload is **spiky and mostly idle**: you pay per RU consumed, with near-zero idle cost. Provisioned (reserved RU/s billed 24/7) is the right model for **steady production traffic**, not a mostly-idle dev database.
- **MongoDB API** so the catalog's existing data-access code carries over by **changing only the connection string**, not the data layer.
- **429 throttling** (exceeding available throughput) is **expected behaviour**, handled by **client-side retry with backoff** — the platform's resilience policies do this transparently (see the [Fault Tolerance with Polly ADR](../adr/ADR-003-fault-tolerance-with-polly.md)).
- The **partition key** is a deliberate data-modelling choice made during the **data-migration work**, not here — so this module creates the account and database, and the application/seeder creates collections (with their keys) at runtime.

Note the account name is **globally unique and lowercase** (it becomes part of the endpoint hostname).

#### Build

This step adds a reusable **cosmosdb module** and its **dev live unit**:

- **`infrastructure/modules/cosmosdb/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `account_name` (globally unique, lowercase, 3–44 chars), `database_name`, `mongo_server_version` (default `"7.0"`), and `tags`.
  - **`main.tf`** — an `azurerm_cosmosdb_account` with `kind = "MongoDB"`, `offer_type = "Standard"`, the **`capabilities { name = "EnableServerless" }`** serverless mode, the MongoDB server version, `consistency_policy` of **`Session`** (the default — reads-your-own-writes within a session, balancing consistency and performance), and a single-region `geo_location`. It carries **`lifecycle { prevent_destroy = true }`** to protect this stateful data resource. A second resource, `azurerm_cosmosdb_mongo_database`, creates the **`antkart-products`** database. Collections and partition keys are **not** defined here — the app/seeder creates them at runtime.
  - **`outputs.tf`** — `id`, `name`, `endpoint`, and `database_name`. The **connection string and keys are intentionally not output** (they are secrets stored in Key Vault later — a comment says exactly that).
- **`infrastructure/environments/dev/cosmosdb/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the other units).
  - `terraform { source = "../../../modules/cosmosdb" }`.
  - `inputs`: `account_name = "cosmos-antkart-dev"`, `database_name = "antkart-products"`, matching tags.

> **`prevent_destroy` choice:** for a disposable dev resource this is acceptable either way; it is **enabled here** to protect the catalog data from an accidental teardown. To intentionally remove the account during teardown, drop the guard first.

#### Execute

Run from the Cosmos DB unit's folder:

```powershell
cd infrastructure/environments/dev/cosmosdb

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the account and database (this takes SEVERAL MINUTES)
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **2 to add** (the account and the Mongo database), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates both resources.

> **Cosmos account creation is slow** — expect it to take **several minutes**. Repeated `Still creating...` messages during `apply` are normal, not a hang.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Account: name, kind (MongoDB), and the serverless capability
az cosmosdb show --resource-group $RG --name "cosmos-antkart-dev" `
  --query "{name:name, kind:kind, capabilities:capabilities[].name}" -o json

# The MongoDB database exists
az cosmosdb mongodb database list --resource-group $RG --account-name "cosmos-antkart-dev" `
  --query "[].name" -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 2 added, 0 changed, 0 destroyed.`
- `az cosmosdb show` reports `name = cosmos-antkart-dev`, `kind = MongoDB`, and `capabilities` includes **`EnableServerless`**.
- `az cosmosdb mongodb database list` shows **`antkart-products`**.

> **Where the connection string lives:** the Cosmos connection string (and account keys) are **not** in Terraform outputs or the repo. They are retrieved later and stored in **Key Vault** during the secrets / data-migration step, and the application reads them from there at runtime.

### 9. Service Bus

> Background reading: [Reliable Messaging with Azure Service Bus](messaging-concepts.md) — message brokers, queue vs topic/subscriptions, at-least-once delivery & idempotency, dead-lettering, tiers, and Service Bus vs Event Grid vs Event Hubs.

#### Understand

**Azure Service Bus** is a fully-managed **enterprise message broker** for messages that **must not be lost** — commands and business data. (Contrast with **Event Grid**, the lightweight push-notification service for announcing that *something happened*; Service Bus carries the durable work.)

**Queue vs. topic — two messaging shapes:**

- **Queue** — **point-to-point / competing consumers**: exactly **one** consumer processes each message. Used for **commands**, which have a single owner.
- **Topic + subscriptions** — **publish/subscribe**: **every subscription receives its own copy** of each message. Used for **integration events**, which have many independent listeners (adding a listener never affects the others).

**At-least-once delivery → idempotent consumers.** Service Bus guarantees a message is delivered *at least* once, which means it may **occasionally arrive twice** (e.g. a consumer crashes after processing but before acknowledging). Consumers must therefore be **idempotent** — processing the same message twice has the same effect as once.

**Dead-lettering.** Every queue and subscription has a built-in **dead-letter sub-queue**. After a message exceeds its **max delivery attempts**, it is moved there instead of being dropped, so it can be **inspected and resubmitted** — nothing is silently lost.

**Tier and cost.** The **Standard** tier is **required for topics** (Basic offers queues only). It is a flat **~US$10/month** — the single largest cost of the current infrastructure.

> The application's messaging library may provision **additional topology at runtime** in the code phase. The entities created here establish the **core, IaC-managed backbone**.

#### Build

This step adds a reusable **servicebus module** and its **dev live unit**:

- **`infrastructure/modules/servicebus/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `namespace_name` (globally unique), `queue_names` (default `["order-commands"]`), `topic_name` (default `"integration-events"`), `subscription_names` (default `["products", "notification"]`), and `tags`.
  - **`main.tf`** — an `azurerm_servicebus_namespace` (`sku = "Standard"`, **`local_auth_enabled = false`** so the data plane accepts only Microsoft Entra identities — no SAS connection strings, consistent with the secret-less model — and `minimum_tls_version = "1.2"`); the queue(s) via `azurerm_servicebus_queue` (`for_each`); the topic via `azurerm_servicebus_topic`; and the subscriptions via `azurerm_servicebus_subscription` (`for_each`, each with **`max_delivery_count = 10`** and a comment that exceeding it dead-letters the message). The queue-vs-topic mapping (commands have one owner; events have many listeners) is explained inline.
  - **`outputs.tf`** — `id`, `name`, and `hostname` (`<name>.servicebus.windows.net`, for the application's identity-based connection later). **No connection strings** — a comment notes data-plane access is via Entra roles granted in a later step.
- **`infrastructure/environments/dev/servicebus/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the other units).
  - `terraform { source = "../../../modules/servicebus" }`.
  - `inputs`: `namespace_name = "sb-antkart-dev"`, matching tags; the queue/topic/subscription names use the module defaults.

#### Execute

Run from the Service Bus unit's folder:

```powershell
cd infrastructure/environments/dev/servicebus

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the namespace, queue, topic, and subscriptions
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **5 to add** (namespace + 1 queue + 1 topic + 2 subscriptions), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the entities and writes this unit's state to the backend.

> The Standard namespace is a flat **~US$10/month** — budget for it.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"
$NS = "sb-antkart-dev"

# Namespace: name, SKU, status
az servicebus namespace show --resource-group $RG --name $NS `
  --query "{name:name, sku:sku.name, status:status}" -o json

# Queues (expect order-commands)
az servicebus queue list --resource-group $RG --namespace-name $NS --query "[].name" -o table

# Topics (expect integration-events)
az servicebus topic list --resource-group $RG --namespace-name $NS --query "[].name" -o table

# Subscriptions on the topic (expect products, notification)
az servicebus topic subscription list --resource-group $RG --namespace-name $NS `
  --topic-name "integration-events" --query "[].name" -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 5 added, 0 changed, 0 destroyed.`
- `az servicebus namespace show` reports `name = sb-antkart-dev`, `sku = Standard`, `status = Active`.
- the queue list shows `order-commands`; the topic list shows `integration-events`; the subscription list shows `products` and `notification`.

> **No connection strings** are output or stored. Application identities are granted Service Bus **data-plane roles** in the managed-identity step, and each service authenticates with **its own identity** (token auth), connecting via the namespace hostname — never a SAS key.

### 10. Event Grid

> Background reading: [Azure Functions & Event Grid Concepts](serverless-eventing-concepts.md) — push-based reactive eventing, retry/dead-lettering, and how Functions and Event Grid partner.

#### Understand

**Event Grid** is **push-based, near-real-time event routing** (full detail in the [serverless-eventing concepts guide](serverless-eventing-concepts.md)): publishers send events to a **topic**; **subscriptions** push them to **handlers**; events that can't be delivered are **retried with backoff (up to 24 hours)** and can be **dead-lettered to storage**. In one line: where **Service Bus** carries durable work that **must not be lost** (consumers *pull*), **Event Grid** carries lightweight **"this happened"** notifications (it *pushes*).

This step creates the **custom topic** — the **publish endpoint** that producers send events to. **Event subscriptions are created later**, once real handlers exist: a subscription needs a concrete destination, so wiring one now would point at nothing.

**Cost:** pay-per-operation — effectively **free at dev volumes**, with **no idle cost**.

#### Build

This step adds a reusable **eventgrid module** and its **dev live unit**:

- **`infrastructure/modules/eventgrid/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `topic_name`, and `tags`.
  - **`main.tf`** — an `azurerm_eventgrid_topic` with **`local_auth_enabled = false`** (key-based publishing disabled — publishers authenticate with their Microsoft Entra identity, consistent with the secret-less model) and **`input_schema = "EventGridSchema"`** (Event Grid's native schema; **CloudEvents** is the open-standard alternative — either works, the platform uses the native schema). Comments explain the push model and why subscriptions come later.
  - **`outputs.tf`** — `id`, `name`, and `endpoint`. **No access keys** — a comment notes publishing uses Entra identities granted the **EventGrid Data Sender** role in a later step.
- **`infrastructure/environments/dev/eventgrid/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the other units).
  - `terraform { source = "../../../modules/eventgrid" }`.
  - `inputs`: `topic_name = "evgt-antkart-dev"`, matching tags.

#### Execute

Run from the Event Grid unit's folder:

```powershell
cd infrastructure/environments/dev/eventgrid

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the Event Grid topic
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add** (the topic), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the topic and writes this unit's state to the backend.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Topic name, publish endpoint, and provisioning state
az eventgrid topic show --name "evgt-antkart-dev" --resource-group $RG `
  --query "{name:name, endpoint:endpoint, provisioningState:provisioningState}" -o json
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az eventgrid topic show` reports `name = evgt-antkart-dev`, `provisioningState = Succeeded`, and an `endpoint` URL (the publish endpoint).

> **No access keys** are output or stored. Publishing identities are granted the **EventGrid Data Sender** role on this topic in the managed-identity step, and each publisher authenticates with **its own identity** — never an access key.

### 11. Function App (Hosting)

> Background reading: [Azure Functions & Event Grid Concepts](serverless-eventing-concepts.md) — what serverless means, the trigger model, the instance lifecycle and cold starts, and the Consumption plan.

#### Understand

This step provisions the **serverless home** the notification function deploys into later (concepts in the [serverless-eventing guide](serverless-eventing-concepts.md)). The function **code** is deployed in a later phase; here we build **where it lands**. Three pieces, each with a reason:

- **Consumption plan** (`azurerm_service_plan`, SKU **`Y1`**) — **pay-per-execution, scale-to-zero** hosting. Right for an event-driven, spiky, mostly-idle workload like notification.
- **Dedicated storage account** — the Functions **runtime's internal plumbing** (trigger state, execution leases, host metadata). It is **required by the platform**, not application data, so it gets its own account.
- **Function App shell** — created now with the **runtime settings**, so application code can be deployed into it later.

**An honest exception.** On the Consumption plan the runtime's storage connection (`AzureWebJobsStorage`) uses the **storage account's shared key** — a **deliberate, narrowly-scoped exception** to the platform's Entra-only pattern. It applies **only to the runtime's internal plumbing**, never to application data or to other services. (Identity-based storage connections are the production hardening path.) **Application-level** access to other services stays **identity-based**.

This unit depends on **both** the **resource group** *and* the **observability** unit — it needs the App Insights **connection string** so the Function App reports telemetry from day one. This is the **multi-dependency pattern**: Terragrunt applies both upstream units first and passes their outputs in.

#### Build

This step adds a reusable **function-app module** and its **dev live unit**:

- **`infrastructure/modules/function-app/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `function_app_name`, `storage_account_name` (globally unique, lowercase alphanumeric, 3–24 chars), `app_insights_connection_string` (**`sensitive = true`**), and `tags`.
  - **`main.tf`** — an `azurerm_service_plan` (`os_type = "Linux"`, `sku_name = "Y1"`); an `azurerm_storage_account` dedicated to the runtime (`Standard_LRS`, `min_tls_version = "TLS1_2"`, `allow_nested_items_to_be_public = false`, with the documented-exception comment about shared key + the runtime); and an `azurerm_linux_function_app` linked to the plan and storage, running the **.NET 9 isolated** worker (`application_stack { dotnet_version = "9.0", use_dotnet_isolated_runtime = true }`), with the **App Insights connection string** in `app_settings`, **`https_only = true`**, and an **`identity { type = "SystemAssigned" }`** block (a comment notes this identity receives data-plane roles in a later step).
  - **`outputs.tf`** — `id`, `name`, `default_hostname`, and the system-assigned identity's **`principal_id`** (consumed by the role-assignment step). No keys or connection strings.
- **`infrastructure/environments/dev/function-app/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - **two dependencies** — `dependency "resource_group"` and `dependency "observability"` (each with `mock_outputs`; the observability mock provides a placeholder connection string so `init`/`plan`/`validate` work before observability is applied).
  - `terraform { source = "../../../modules/function-app" }`.
  - `inputs`: `function_app_name = "func-antkart-notifications-dev"`, `storage_account_name = "stantkartfuncdev"`, `app_insights_connection_string` from the observability dependency, matching tags.

#### Execute

Run from the Function App unit's folder:

```powershell
cd infrastructure/environments/dev/function-app

# 1. Init — generates backend.tf/provider.tf and resolves BOTH dependencies
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the plan, storage account, and Function App
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` **and** `observability` dependencies.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **3 to add** (the service plan, the storage account, and the Function App), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the three resources. (Terragrunt applies the resource group and observability units first.)

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"
$FN = "func-antkart-notifications-dev"

# Function App: name, state (expect Running), kind
az functionapp show --resource-group $RG --name $FN `
  --query "{name:name, state:state, kind:kind}" -o json

# Runtime config (linux FX version / app settings presence)
az functionapp config show --resource-group $RG --name $FN --query "{linuxFxVersion:linuxFxVersion}" -o json

# Function list — EMPTY until code is deployed (expected at this stage)
az functionapp function list --resource-group $RG --name $FN -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 3 added, 0 changed, 0 destroyed.`
- `az functionapp show` reports `name = func-antkart-notifications-dev`, `state = Running`, and a `kind` of `functionapp,linux`.
- the **function list is empty** — that is expected: the shell exists, the code is deployed in a later phase.

### 12. Entra ID App Registration & Roles

> Background reading: [Identity Concepts](identity-concepts.md) — app registrations (identity + contract), tokens and claims (issuer, audience, roles), and how APIs validate them.

#### Understand

An **app registration** registers an application in **Microsoft Entra ID** (concepts in the [identity guide](identity-concepts.md)). Registering gives the app two things:

- **An identity** — a **client / application ID** the app authenticates as.
- **A contract** — the **app roles** it defines (e.g. `admin`, `user`) and the **audience** (an identifier URI) that APIs validate incoming tokens against.

**App roles → a flat `roles` claim.** The roles defined here surface in an issued token as values in a **flat `roles` claim** (e.g. `"roles": ["admin"]`), which the application reads to authorize. (In one line: some other identity providers nest role claims under a different path — so consumers must read the **correct claim path for the issuer**.)

**Two providers.** Every resource module so far used the **`azurerm`** provider (Azure *resources*). This step uses the **`azuread`** provider, which talks to **Microsoft Graph** — the *directory* — to create directory objects. They are different providers with different permissions (see Execute).

#### Build

This step adds a reusable **app-registration module** and its **dev live unit**:

- **`infrastructure/modules/app-registration/`**:
  - **`versions.tf`** — a `terraform` block declaring the required **`azuread`** provider (`hashicorp/azuread`), plus the `provider "azuread" {}` configuration (it authenticates from the same context as `azurerm`).
  - **`variables.tf`** — `display_name`, `identifier_uri` (optional — derived as `api://<display_name>` if null), `app_roles` (a default list of **admin** and **user** objects with **fixed GUID ids** so they don't drift, and `allowed_member_types = ["User", "Application"]`), and `tags`.
  - **`main.tf`** — an `azuread_application` with the **app roles**, the **identifier URI** (the audience), and an **exposed API** (`access_as_user` OAuth2 scope); and an `azuread_service_principal` referencing it so the app exists as a **usable principal** in the tenant. A comment explains that **no client secret** is created — an API that *validates* tokens doesn't need one (callers that *request* tokens manage their own credentials), keeping the secret-less posture.
  - **`outputs.tf`** — `client_id`, `object_id`, the service principal's `object_id`, and the defined `app_role_values`. No secrets.
- **`infrastructure/environments/dev/app-registration/terragrunt.hcl`**:
  - `include "root"` (state still uses the azurerm backend).
  - **no `resource_group` dependency** — an app registration is a **directory object** (it lives in the tenant, not in a resource group), so there's no name/location to consume. A comment notes this difference.
  - `terraform { source = "../../../modules/app-registration" }`.
  - `inputs`: `display_name = "antkart-api-dev"` (the admin/user app roles come from the module default).

#### Execute

> **Provider-permission caveat — read before applying.** This unit uses the **`azuread`** provider, which calls **Microsoft Graph** (the directory), **not** the Azure resource manager. The automation service principal currently holds **subscription** roles (Contributor + RBAC Administrator) but may **not** have **directory** permission to create app registrations. If `terragrunt apply` fails with an **insufficient-privileges / Graph** error, fix it by one of:
> - grant the automation SP the **Application Administrator** Entra role (or the Graph **`Application.ReadWrite.All`** application permission), **or**
> - run **this one unit** as the **signed-in admin user** (who has directory rights).
>
> Don't assume it will just work — decide and document which path you took.

```powershell
cd infrastructure/environments/dev/app-registration

# 1. Init — downloads the azuread provider and wires the backend
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the app registration + service principal
terragrunt apply
```

What each does:
- **`terragrunt init`** — downloads the **`azuread`** provider and initializes the backend.
- **`terragrunt plan`** — a correct plan reports the **application + service principal** (with the **admin** and **user** app roles and the exposed API scope) being **added**, **0 to destroy**.
- **`terragrunt apply`** — creates the directory objects (subject to the permission caveat above).

#### Verify

```powershell
# Confirm the registration and its app roles (use the client_id from the outputs)
az ad app show --id "<client-id>" `
  --query "{displayName:displayName, appId:appId, identifierUris:identifierUris, roles:appRoles[].value}" -o json

# Or find it by name
az ad app list --display-name "antkart-api-dev" --query "[].{displayName:displayName, appId:appId}" -o table
```

A **correct result**:
- `az ad app show` reports `displayName = antkart-api-dev`, an `appId` (the client id), the `identifierUris` (`api://antkart-api-dev`), and `roles` containing **`admin`** and **`user`**.
- `az ad app list` finds the registration by name.

> The output **`client_id`** is a **real value** (not a placeholder) consumed by the application's auth configuration in the data/identity migration phase — the audience/authority the API validates tokens against.

### 13. Data-Plane Role Assignments

> Background reading: [Identity Concepts](identity-concepts.md) — managed identities vs service principals, workload identity federation (the no-stored-secret chain), and `DefaultAzureCredential`.

#### Understand

This is where the **secret-less model completes** (concepts in the [identity guide](identity-concepts.md)). Every resource so far was configured to **accept only Microsoft Entra identities** — keys/SAS disabled (Key Vault uses RBAC, Service Bus and Event Grid have `local_auth_enabled = false`, Cosmos uses Entra auth). Now the **consuming workload's managed identity** — the Function App's system-assigned identity from Step 11 — is granted the **specific, least-privilege data-plane roles** it needs to actually use those resources.

**Control plane vs. data plane:**

- **Control-plane** roles (Contributor, Key Vault Administrator, …) **manage the resource** — create, configure, delete it.
- **Data-plane** roles **use the data inside** — read a secret, receive/send a message, publish an event.

These grants are all **data-plane**, each **scoped to the specific resource, never the subscription** (least privilege in both the role *and* the scope):

- **Key Vault Secrets User** on the Key Vault — **read** secret values (not Secrets *Officer*, which could also create/delete/manage secrets).
- **Azure Service Bus Data Receiver** + **Azure Service Bus Data Sender** on the Service Bus namespace — **receive and send** messages (not *Owner*).
- **EventGrid Data Sender** on the Event Grid topic — **publish** events (not *Contributor*).

> **AcrPull is not here.** Pull access for the **AKS kubelet identity** is a **different identity, created with the cluster**, so it is deferred to the **AKS milestone**.

#### Build

This step adds a reusable **role-assignments module** and its **dev live unit**:

- **`infrastructure/modules/role-assignments/`**:
  - **`variables.tf`** — `principal_id` (the managed identity), `key_vault_id`, `servicebus_namespace_id`, `eventgrid_topic_id`.
  - **`main.tf`** — four `azurerm_role_assignment` resources: **Key Vault Secrets User** (scope = Key Vault), **Azure Service Bus Data Receiver** and **Azure Service Bus Data Sender** (scope = Service Bus namespace), and **EventGrid Data Sender** (scope = Event Grid topic) — each with `principal_id` from the variable and `principal_type = "ServicePrincipal"` (so the provider doesn't have to look up a just-created identity that may not have replicated yet). Comments explain each role's least-privilege choice and scope.
  - **`outputs.tf`** — the four role assignment ids, for reference / audit.
- **`infrastructure/environments/dev/role-assignments/terragrunt.hcl`**:
  - `include "root"`.
  - **four dependencies** — `function_app` (for `principal_id`), `key_vault` (for `id`), `servicebus` (for `id`), and `eventgrid` (for `id`), each with `mock_outputs`. This unit **composes several upstream units' outputs** into one set of grants.
  - `terraform { source = "../../../modules/role-assignments" }`, with `inputs` wiring each value from its dependency.

> **Permissions & propagation.** Creating role assignments requires the automation SP to hold **Role Based Access Control Administrator** — which it does (Step 1). After apply, **RBAC propagation can take a minute or two** before the new access is effective.

#### Execute

```powershell
cd infrastructure/environments/dev/role-assignments

# 1. Init — generates backend.tf/provider.tf and resolves all four dependencies
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the four role assignments
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider and resolves the `function_app`, `key_vault`, `servicebus`, and `eventgrid` dependencies.
- **`terragrunt plan`** — a correct plan reports **4 to add** (the four role assignments), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the assignments. (Terragrunt applies the four upstream units first.)

#### Verify

```powershell
# List the assignments for the Function App's managed identity (use its principal id)
az role assignment list --assignee "<principal-id>" --all `
  --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 4 added, 0 changed, 0 destroyed.`
- the list shows four scoped, least-privilege grants: **Key Vault Secrets User** (Key Vault scope), **Azure Service Bus Data Receiver** and **Azure Service Bus Data Sender** (namespace scope), and **EventGrid Data Sender** (topic scope). Allow a minute or two for RBAC to propagate before access works.

> **This completes the secret-less wiring:** the application authenticates with its **managed identity** and is authorized by these **scoped roles** — **no secrets anywhere**.
>
> **Connection-string-to-Key-Vault flow (intent).** Storing the **Cosmos connection string as a Key Vault secret** happens in the **data-migration phase**; the application then reads it at runtime via its **Key Vault Secrets User** role. The secret is **not created now** — this step only establishes the **access path**.

### 14. Governance, Tagging & Cost Controls

#### Understand

Governance closes the milestone: it makes the environment **well-architected** — cost-aware, reproducible, protected, consistently tagged, and secret-less — and **records the evidence**. This step adds a **cost budget with alerts** and a **shared provider-version pin**, then **audits** the protections, tags, and secret-less posture already in place.

#### Build

- **Shared provider pin.** `environments/dev/root.hcl` now generates a `versions.tf` into every unit pinning **`azurerm ~> 4.76`**, so all units resolve the **same** provider version (resolving the earlier 4.75 / 4.76 drift). The **`azuread ~> 3.0`** constraint stays in the app-registration unit — its only consumer. Pinning gives **reproducible, drift-free** provisioning: without a shared constraint, different units (or runs on different days) could pull different provider versions.
- **Budget + cost alerts.** `infrastructure/modules/governance/` creates an `azurerm_consumption_budget_resource_group` set to **US$200 / month**, with notifications at **50%** and **80%** (actual spend) and **100%** (forecast) emailing a contact address (parameterized as `contact_emails`). FinOps in one idea: a budget does not cap spend — it **watches** it and **alerts** you, so the bill is never a surprise.

#### Execute

**Re-pin providers (follow-up — per unit).** After the shared `versions.tf` change, align each unit's lock file and re-commit it:

```powershell
# Run in each unit folder, e.g. infrastructure/environments/dev/<unit>
terragrunt init -upgrade
# then re-commit the updated .terraform.lock.hcl
```

**Apply the budget.**

```powershell
cd infrastructure/environments/dev/governance
terragrunt init
terragrunt plan      # expect: 1 to add (the budget), 0 to change, 0 to destroy
terragrunt apply
```

> Set `contact_emails` to a **real address** in the unit's `inputs` before applying — that is who receives the alerts.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"
az consumption budget list --resource-group $RG `
  --query "[].{name:name, amount:amount, timeGrain:timeGrain}" -o table
```

A **correct result**: `terragrunt apply` ends with `1 added, 0 changed, 0 destroyed`, and the budget `budget-antkart-dev` appears at amount `200`, `Monthly`.

#### Governance Evidence (Audit)

The Well-Architected / governance evidence for the milestone.

**Stateful-resource protection (`prevent_destroy`).** Every resource that holds data is guarded against accidental teardown:

| Resource | Protection |
|----------|------------|
| Resource group | `lifecycle { prevent_destroy = true }` |
| Cosmos DB account | `lifecycle { prevent_destroy = true }` |
| Key Vault | Soft-delete retention (always on) + purge protection (enabled in production) — a deleted vault is recoverable and its name stays reserved |

Stateless / rebuildable resources (networking, registry, messaging, Function hosting) are intentionally not guarded — they can be recreated without data loss.

**Standard tags.** Every taggable Azure resource carries the standard tags:

| Tag | Value (dev) |
|-----|-------------|
| `environment` | `dev` |
| `project` | `antkart` |
| `managed-by` | `terraform` |

(Directory objects — the app registration — use Entra's list-style tags `["antkart", "dev"]`; role assignments and the budget are not taggable.)

**Secret-less, identity-based posture.** Every data plane accepts Microsoft Entra identities; shared keys / SAS / admin accounts are disabled — the one documented exception being the Functions runtime's internal storage:

| Resource | Key / SAS / admin | Identity-based access |
|----------|-------------------|-----------------------|
| Key Vault | RBAC authorization (access policies off) | Entra — **Key Vault Secrets User** |
| Service Bus | `local_auth_enabled = false` (SAS off) | Entra — **Data Receiver / Data Sender** |
| Event Grid topic | `local_auth_enabled = false` (keys off) | Entra — **EventGrid Data Sender** |
| Container Registry | `admin_enabled = false` | Entra — AcrPull (granted with AKS) |
| Storage (Terraform state) | shared-key access disabled | Entra — Storage Blob Data Contributor |
| Cosmos DB | connection string vaulted (not exposed) | App reads the secret from Key Vault (Secrets User) |
| Communication Services (ACS Email) | keys exist but **not exposed** (no output) | Entra — data-plane role on the Communication Service |
| Storage (Functions runtime) | **shared key enabled — documented exception** | `AzureWebJobsStorage` only (runtime plumbing) |

The automation identity is itself least-privilege (Contributor + Role Based Access Control Administrator, subscription-scoped), and every workload role is scoped to a specific resource — never the subscription.

### 15. Communication Services (ACS Email)

#### Understand

**Azure Communication Services (ACS) Email** is Azure's managed service for sending application email — **no self-hosted SMTP server and no third-party email vendor**. This step provisions the email infrastructure ahead of the application's send path. It has **three resources plus a link**, created in order:

- **Email Communication Service** — the container that owns email **domains**.
- **Email domain (Azure-managed)** — the sender identity. With **`domain_management = "AzureManaged"`**, Azure provisions a **free `*.azurecomm.net` sender subdomain and auto-configures its SPF/DKIM** — so there is **no custom domain to buy and no DNS records to publish**. (The alternative, `CustomerManaged`, requires owning a domain and publishing verification/DKIM/SPF records.)
- **Communication Service** — the resource the application connects to; its **`hostname`** is the endpoint the Email SDK targets.
- **Domain association** — links the managed domain to the Communication Service as an allowed **sender**, so the service may send "from" that domain.

**Authentication — no keys.** ACS exposes connection strings/keys, but the application authenticates to the endpoint with its Microsoft **Entra managed identity**, granted a data-plane role on the Communication Service in the role-assignment step. No key or connection string is output, stored, or committed.

**Cost:** pay-per-message with a free monthly allowance — **effectively free at dev volumes**, no idle cost.

#### Build

This step adds a reusable **communication-services module** and its **dev live unit**:

- **`infrastructure/modules/communication-services/`**:
  - **`variables.tf`** — `resource_group_name`, `name_prefix` (derives the resource names), `data_location` (data-at-rest residency, default `United States`), and `tags`.
  - **`main.tf`** — an `azurerm_email_communication_service`; an `azurerm_email_communication_service_domain` with **`name = "AzureManagedDomain"`** (the provider requires this literal name when `domain_management = "AzureManaged"`) and **`domain_management = "AzureManaged"`**; an `azurerm_communication_service`; and an `azurerm_communication_service_email_domain_association` linking the two. Comments explain ACS Email, the managed sender domain (no DNS), and managed-identity auth. The provider version pin (`~> 4.76`) comes from the shared root, so the module declares none of its own.
  - **`outputs.tf`** — `communication_service_id`, `communication_service_hostname`, `communication_service_endpoint` (`https://<hostname>`), `email_service_id`, `sender_domain` (the `*.azurecomm.net` subdomain), and `sender_address` (`DoNotReply@<managed-subdomain>`). **No keys or connection strings.**
- **`infrastructure/environments/dev/communication-services/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider/version pin.
  - a **`dependency "resource_group"`** consuming the resource group's `name` (with `mock_outputs` for `init`/`plan`/`validate`). ACS is a **global service and takes no `location`** — only the resource group name is needed.
  - `terraform { source = "../../../modules/communication-services" }`.
  - `inputs`: `name_prefix = "antkart-dev"` (yields `acs-antkart-dev` and `acs-email-antkart-dev`), `data_location = "United States"`, matching tags.

#### Execute

```powershell
cd infrastructure/environments/dev/communication-services

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the ACS Email resources
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **4 to add** (email service, managed domain, communication service, domain association), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the four resources and writes this unit's state to the backend.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Communication Service: name, endpoint host, provisioning state
az communication show --name "acs-antkart-dev" --resource-group $RG `
  --query "{name:name, hostName:hostName, provisioningState:provisioningState}" -o json

# Managed sender domain: the auto-issued *.azurecomm.net subdomain
az communication email domain show `
  --email-service-name "acs-email-antkart-dev" --name "AzureManagedDomain" --resource-group $RG `
  --query "{fromSenderDomain:fromSenderDomain, dataLocation:dataLocation}" -o json
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 4 added, 0 changed, 0 destroyed.`
- `az communication show` reports `name = acs-antkart-dev`, `provisioningState = Succeeded`, and a `hostName`.
- `az communication email domain show` reports a `*.azurecomm.net` `fromSenderDomain`.

> **No access keys** are output or stored. The application reaches the endpoint with **its managed identity**, granted a data-plane role on the Communication Service in the role-assignment step — never a key.

---

**Navigation:** **Prerequisite:** [IaC fundamentals](iac-concepts.md) · **Related concepts:** [Networking](networking-concepts.md), [Cosmos DB](cosmosdb-concepts.md), [Messaging](messaging-concepts.md), [Serverless & Eventing](serverless-eventing-concepts.md), [Identity](identity-concepts.md) · [← Development Guide](../../DevelopmentGuide.md)
