# Heroic Runbook — Provisioning a New Environment

**Purpose.** Take an Azure subscription that already hosts the `dev` environment and stand up a
complete, independent second environment — infrastructure, cluster, identity, GitOps and CD —
without touching `dev` and without hand-editing anything twice.

**Audience.** Someone who has never provisioned this platform. Every step states what it does,
what to check before, and what proves it worked.

**Scope.** This runbook is written for `qa` but is **parameterized**. Set `$ENV` once and every
command follows. To build `stage`, `uat` or `prod` later, change that one value and re-run this
document from the top.

---

## 0. How to use this runbook

### 0.1 Set your environment variables — do this in every new shell

PowerShell loses variables when it resets. Re-run this block at the start of every session and
after any restart. Everything below depends on it.

```powershell
# ---- The only line you change per environment -------------------------------
$ENV = "qa"

# ---- Derived names (dev convention: <resource>-antkart-<env>) ----------------
$LOCATION      = "eastus"
$LOCATION_DATA = "eastus2"                       # see 1.4 — Postgres AND Redis
$RG            = "rg-antkart-$ENV-eastus"
$VNET          = "vnet-antkart-$ENV-eastus"
$AKS           = "aks-antkart-$ENV"
$ACR           = "acrantkart$ENV"                # globally unique, no hyphens
$KV            = "kv-antkart-$ENV"               # globally unique
$COSMOS        = "cosmos-antkart-$ENV"           # globally unique
$SB            = "sb-antkart-$ENV"               # globally unique
$EVGT          = "evgt-antkart-$ENV"
$REDIS         = "redis-antkart-$ENV"
$LOG           = "log-antkart-$ENV"
$APPI          = "appi-antkart-$ENV"
$PG            = "psql-antkart-$ENV-eus2"        # globally unique
$FUNC          = "func-antkart-notifications-$ENV"
$FUNC_SA       = "stantkartfunc$ENV"             # globally unique, no hyphens
$BUDGET        = "budget-antkart-$ENV"
$APPREG        = "antkart-api-$ENV"

# ---- State backend (shared account, per-environment container) ---------------
$STATE_RG        = "rg-antkart-tfstate"
$STATE_SA        = "stantkarttfstate"
$STATE_CONTAINER = "tfstate-$ENV"                # NOT "tfstate" — see Phase 0

# ---- Paths ------------------------------------------------------------------
$REPO_ROOT = (Get-Location).Path
$ENVDIR    = Join-Path $REPO_ROOT "infrastructure\environments\$ENV"

Write-Host "Environment=$ENV  RG=$RG  StateContainer=$STATE_CONTAINER"
```

> Run section 0.1 from the repository root. `$ENVDIR` is absolute so that `cd "$ENVDIR\<unit>"`
> works from any directory — a relative path resolves against the current location and
> fails once you are already inside a unit folder.

Confirm the printed line is what you expect before running anything else.

### 0.2 Session split

This is **two sessions**, not one. Say so to yourself now so the second half of day one is not a
disappointment.

| Session | Phases | Outcome |
|---|---|---|
| One | 0 – 4 | Infrastructure provisioned, cluster reachable, identity working, secrets vaulted |
| Two | 5 – 6 | GitOps, CD targeting the new environment, end-to-end verification |

### 0.3 Standing rules

- **Verify, never assume.** Every phase ends with a check. Run it even when you are confident.
- **Plan before apply.** `terragrunt plan` on every unit before the first `apply` of a wave.
- **Stop what you start.** AKS and PostgreSQL are the expensive resources. Phase 7 is teardown.
- **One wave at a time.** Do not run `run-all apply` across the whole tree on a first build.
- **`⚠️ UNVERIFIED`** marks a step written from the repository but not yet executed against a live cluster. Treat it as a starting point, verify the result, and remove the marker once it is proven. Phases 0-4 and 6, and sections 5.1-5.7, carry no markers — they have been run end to end. Sections 5.8 (CD promotion), 5.9 (Notification path), and the optional Phase 6A (API Management edge) remain marked — they are still outstanding.

### 0.4 The five ideas behind every command in this runbook

If Terraform is new to you, these five ideas explain most of what follows.

**1. Terraform describes; it does not script.** You declare the resources you want and
Terraform works out the calls to get there. Running the same file twice changes nothing
the second time — that property is called idempotence, and it is why re-running a failed
step is safe.

**2. State is the memory, and it is the dangerous part.** Terraform records what it
created in a state file. It compares that record against your files to decide what to
change. Point a new environment at another environment's state and Terraform concludes
the live resources are *its* resources — and offers to reshape them. That is the whole
reason Phase 0 exists.

**3. Terragrunt is a wrapper that removes repetition.** Terraform alone would need the
backend and provider settings copied into all 18 units. Terragrunt keeps them once in
`root.hcl` and generates the rest at `init` time. That is why `backend.tf` and
`provider.tf` are deleted in Phase 1 — they are generated files, not source.

**4. A "unit" is one folder, one state file.** Each folder under the environment is
applied independently and owns its own state blob. Small blast radius: a mistake in one
unit cannot corrupt another.

**5. `plan` shows, `apply` does.** Plan is a dry run printing what would change. It is
free and safe. Every apply in this runbook is preceded by a plan for one reason — the
plan is where you catch the wrong environment before it costs you.

### 0.5 The one-way doors

Three things in this runbook cannot be undone cheaply. They are called out where they occur.

| Door | Why it is one-way | Phase |
|---|---|---|
| Key Vault purge protection | Azure will not let it be disabled once enabled; a deleted vault's name is locked for the retention window | 1.3 |
| Terraform state container name | Changing it after apply orphans every state blob | 0 |
| Globally unique resource names | Taken names cannot be reused while soft-deleted | 1.2 |

### 0.6 Where everything lives

Two folders do different jobs, and the difference is the core idea.

**`infrastructure/modules/`** holds 18 reusable blueprints — the `.tf` files that describe
*how* to build each resource type. They are shared by every environment and are never
copied. Nothing in this runbook edits them.

**`infrastructure/environments/`** holds one folder per environment. Each unit inside is a
single `terragrunt.hcl` that points at a module and supplies *this* environment's values.

Compare one resource:

```
infrastructure/modules/redis/             <- the blueprint (shared)
├── main.tf                               declares azurerm_managed_redis
├── variables.tf                          what it accepts (name, location, sku...)
└── outputs.tf                            what it hands to other units

infrastructure/environments/dev/redis/    <- the instance (per environment)
└── terragrunt.hcl                        "use ../../../modules/redis, name it
                                          redis-antkart-dev, put it in eastus2"
```

That is the whole pattern, repeated 18 times.

**Today:**

```
infrastructure/
├── modules/                              18 blueprints — shared
└── environments/
    └── dev/                              the only environment
        ├── root.hcl                      backend + provider settings for THIS environment
        ├── aks/terragrunt.hcl
        ├── redis/terragrunt.hcl
        └── ... 16 more units
```

**After this runbook:**

```
infrastructure/
├── modules/                              UNCHANGED — still 18, still shared
└── environments/
    ├── dev/
    │   ├── root.hcl                      state_container = "tfstate"
    │   └── ... 18 units
    └── qa/                               NEW — a sibling of dev, not a child
        ├── root.hcl                      state_container = "tfstate-qa" <-- the isolation lever
        └── ... 18 units, same names, qa values
```

Note `root.hcl` sits **inside** each environment folder, not above them. That is why each
environment can carry its own backend settings — and why the copy must be edited before
it is ever run.

**Why the state container is the whole safety story.** Terragrunt names each state blob
after the unit's path relative to `root.hcl`. Since `qa/redis` and `dev/redis` both resolve
to `redis`, their blob paths are *identical*. Different containers are what keeps them
apart:

```
Storage account: stantkarttfstate
├── tfstate/                              dev's 18 blobs — NEVER TOUCHED
│   ├── aks/terraform.tfstate
│   ├── redis/terraform.tfstate
│   └── ... 16 more
└── tfstate-qa/                           NEW container, identical internal layout
    ├── aks/terraform.tfstate
    ├── redis/terraform.tfstate
    └── ... 16 more
```

Same blob names, different containers, zero collision. Leave the container as `tfstate`
and QA writes straight over dev's records.

**Outside `infrastructure/`.** Two more places gain files, both in session two (Phase 5):

```
deploy/
├── helm/
│   ├── antkart-service/                  one generic chart — shared by all services and environments
│   └── values/                           6 dev value files today; QA values land here
│                                         (layout decided in Phase 5.3)
└── argocd/
    ├── appproject-antkart.yaml           the guard rails — what Argo may deploy and where
    └── applications/                     6 Application manifests, currently pointing at dev values

.github/workflows/                        12 workflows (6 CI + 6 CD)
                                          CD workflows gain QA targeting (Phase 5.5)
```

**What this runbook does NOT change:** `infrastructure/modules/`, the six service
codebases, the Helm chart itself, and every file under `environments/dev/`.

---

## 1. Prerequisites and decisions

### 1.1 Prerequisite checks

Run all four. Do not proceed past a failure.

```powershell
# (a) Tooling present
az version
terraform version
terragrunt --version
kubectl version --client
helm version

# (b) Correct subscription selected
az account show --query "{name:name, id:id, tenant:tenantId}" -o table

# (c) The Terraform service principal still has its two subscription-scoped roles
#     (Contributor + Role Based Access Control Administrator). Replace the appId.
az role assignment list --assignee "<terraform-sp-appId>" `
  --query "[].{role:roleDefinitionName, scope:scope}" -o table

# (c2) The Terraform service principal needs an Entra DIRECTORY role to create
#      app registrations. Azure RBAC does not grant this.
az rest --method GET `
  --uri "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?`$filter=principalId eq '<terraform-sp-objectId>'" `
  --query "value[].roleDefinitionId" -o tsv

# (d) The four ARM_* variables are set for this shell
Get-ChildItem Env: | Where-Object { $_.Name -like "ARM_*" } | Select-Object Name
```

> **Two permission planes, not one.** Azure RBAC governs subscriptions and resources;
> Entra ID governs the directory — users, groups, app registrations. Contributor grants
> nothing in the directory. A service principal can create a resource group and still
> fail creating an app registration with:
>
> `Authorization_RequestDenied: Insufficient privileges to complete the operation`
>
> Fix: assign **Cloud Application Administrator** to the service principal in the portal
> under Entra ID → Roles and administrators. Allow ~2 minutes for propagation. The role
> assignment picker shows only users until you search by the SP's display name or appId.
>
> This is invisible in the first environment if its app registration was created
> interactively by an administrator rather than by the service principal.

> **(c3) Key Vault data plane.** With `enable_rbac_authorization = true`, Key Vault splits
> into two planes. Contributor covers the control plane — creating the vault, setting its
> properties. It grants nothing on the data plane, where secrets are read and written. A
> service principal can therefore create a vault and immediately fail writing a secret into
> it:
>
> ```
> Status=403 Code="Forbidden" ... Action: 'Microsoft.KeyVault/vaults/secrets/getSecret/action'
> Assignment: (not found)  InnerError={"code":"ForbiddenByRbac"}
> ```
>
> `Assignment: (not found)` means no data-plane role exists at all.
>
> Fix — grant **Key Vault Secrets Officer** (not Secrets User; Terraform must write):
>
> ```powershell
> az role assignment create `
>   --assignee-object-id "<terraform-sp-objectId>" `
>   --assignee-principal-type ServicePrincipal `
>   --role "Key Vault Secrets Officer" `
>   --scope $(az keyvault show -n $KV --query id -o tsv)
> ```
>
> Allow ~2 minutes for propagation, then re-run the unit. The vault is already in state, so
> only the secret is created.
>
> **The operator needs this as well.** The service principal grant covers Terraform. The
> human running this runbook is a separate principal and gets its own 403 when listing or
> writing secrets in Phase 4:
>
> ```
> Caller: appid=04b07795-8ddb-461a-bbee-02f9e1bf7b46 ... Assignment: (not found)
> ```
>
> That `appid` is the Azure CLI itself; the `oid` is the signed-in user. Grant yourself
> Secrets Officer on the new vault:
>
> ```powershell
> $myOid = az ad signed-in-user show --query id -o tsv
> az role assignment create `
>   --assignee-object-id $myOid `
>   --assignee-principal-type User `
>   --role "Key Vault Secrets Officer" `
>   --scope $(az keyvault show -n $KV --query id -o tsv)
> ```
>
> Three principals need data-plane access on a new vault, and each is a separate grant:
> the Terraform service principal (writes secrets during apply), the operator (seeds and
> verifies secrets in Phase 4), and the workload identities (read secrets at runtime —
> these are the only ones already in code, via the role-assignments unit).

`(c)` must show **Contributor** and **Role Based Access Control Administrator** at subscription
scope. `(d)` must list `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, `ARM_SUBSCRIPTION_ID`,
`ARM_TENANT_ID`. If they are missing, re-set them — see
[Infrastructure Guide § Terraform Identity & Access](infrastructure-guide.md).

### 1.1.1 Why these permission gaps are invisible in the first environment

> Three separate permission planes are involved, and they are easy to mistake for one:
>
> | Plane | Governs | Role needed |
> |---|---|---|
> | Azure RBAC | Subscriptions, resource groups, resources | Contributor + RBAC Administrator |
> | Entra ID directory | Users, groups, app registrations | Cloud Application Administrator |
> | Key Vault data plane | Reading and writing secrets | Key Vault Secrets Officer |
>
> A first environment often works without the second and third because an administrator
> performed those steps interactively — creating the app registration by hand, writing the
> first secrets under their own credentials. The service principal never needed the
> permission, so the gap was never visible.
>
> Building a second environment is what surfaces it. If the source environment's Key Vault
> shows a human account holding Secrets Officer and no service principal, that is the
> signature of exactly this.

### 1.2 Check globally unique names before you create anything

Eight resource types take names from a global namespace. A name that is taken — or soft-deleted
elsewhere — fails the apply *midway through a wave*, which is the worst place to find out.

```powershell
# Storage accounts (function app storage)
az storage account check-name --name $FUNC_SA -o table

# Container registry
az acr check-name --name $ACR -o table

# Key Vault — a soft-deleted vault of the same name blocks creation
az keyvault list-deleted --query "[?name=='$KV'].{name:name, purgeDate:properties.scheduledPurgeDate}" -o table

# Service Bus namespace
az servicebus namespace exists --name $SB -o table

# Cosmos DB account name (empty output = available)
az cosmosdb check-name-exists --name $COSMOS

# Azure Managed Redis — name is globally unique (it forms the hostname)
az resource list --resource-type "Microsoft.Cache/redisEnterprise" `
  --query "[?name=='$REDIS'].{name:name, rg:resourceGroup}" -o table

# PostgreSQL flexible server — name is subscription+region unique; list existing
az postgres flexible-server list --query "[?name=='$PG'].name" -o tsv
```

**Correct result:** every check reports the name as available and `az keyvault list-deleted`
returns nothing for `$KV`.

If a Key Vault of this name is soft-deleted and purge protection was **off**, purge it:

```powershell
az keyvault purge --name $KV --location $LOCATION
```

If purge protection was **on**, the name is unusable until the retention window expires. Choose a
different `$ENV` suffix or a different vault name and record why in an ADR.

### 1.3 Decisions — dev conventions, with one deliberate deviation

| # | Decision | dev value | This environment | Rationale |
|---|---|---|---|---|
| A | Naming convention | `<resource>-antkart-dev` | `<resource>-antkart-$ENV` | Follow dev. Predictable, greppable. |
| B | Container registry | `acrantkartdev`, dedicated | **Dedicated** `acrantkart$ENV` | Follow dev. A shared ACR couples environments and complicates the AcrPull grant. |
| C | Key Vault purge protection | `true` | **`false`** | **Deviation — see below.** |
| D | Region | `eastus`, Postgres in `eastus2` | Same | Follow dev. Re-verify Postgres availability (1.4). |
| E | State isolation | container `tfstate` | container `tfstate-$ENV` | Option A. Dev's state key expression is never touched. |

**Decision C is the one place "follow dev" is wrong, and it is worth understanding.**

`dev` has `purge_protection_enabled = true` — not by design, but because it was switched on out of
band on the live vault and Azure will not allow turning it off. The module's own variable
description records the intent: *a genuinely disposable environment may set it false to allow early
purge and recreate during teardown.* That is exactly what a QA environment is.

With it `true`, tearing down and rebuilding this environment locks the vault name for the soft-delete
retention window — you cannot recreate it under the same name for up to seven days. That makes the
environment un-rebuildable, which defeats its purpose. This is tracked as **KI-007**.

The trade-off is real: production wants irreversibility, disposable environments want repeatability.
Record the choice as an ADR — *production irreversibility versus rebuild repeatability* — rather
than as a silent config difference.

Set it in `$ENVDIR/key-vault/terragrunt.hcl` (dev has it at line 65):

```hcl
purge_protection_enabled = false
```

### 1.4 Confirm the Postgres region workaround still applies

`dev` provisions PostgreSQL in `eastus2` because the required offer was restricted in `eastus`.
Check whether that is still true rather than copying the workaround blindly:

```powershell
az postgres flexible-server list-skus --location eastus -o table
```

If the SKU list returns normally, set `$LOCATION_DATA = "eastus"` and note the change. If it errors or
returns restricted offers, keep `eastus2`.

---

## Phase 0 — Isolate the state backend

**This is the highest-risk phase in the runbook and the shortest. Read 0.1 before running it.**

### Understand

Terragrunt derives each unit's state blob path from `path_relative_to_include()` — the unit's path
relative to the folder containing `root.hcl`. In this repo `root.hcl` lives **inside**
`infrastructure/environments/dev/`, so `dev/aks` resolves to `aks` and its state key is
`aks/terraform.tfstate`.

Copy that folder to `environments/qa/` and `root.hcl` comes with it. `qa/aks` **also** resolves to
`aks`, producing the identical key. Same container, same key, same blob. Terragrunt would attach the
new environment to dev's live state and Terraform would plan to recreate the running platform.

The fix is to change the **container**, not the key expression. Dev's 18 state blobs stay exactly
where they are at `<unit>/terraform.tfstate` in `tfstate`; the new environment gets its own
container with the same internal layout.

### Execute

```powershell
# Create the per-environment state container (Entra auth — shared keys are disabled)
az storage container create `
  --name $STATE_CONTAINER `
  --account-name $STATE_SA `
  --auth-mode login
```

### Verify

```powershell
# The new container exists
az storage container show --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table

# It is empty
az storage blob list --container-name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table

# CRITICAL: dev's 18 state blobs are untouched
az storage blob list --container-name "tfstate" --account-name $STATE_SA --auth-mode login `
  --query "length(@)" -o tsv
```

**Correct result:** the new container exists and is empty; the `tfstate` container still reports
**18** blobs. If that number is not 18, stop and investigate before going further.

---

## Phase 1 — Create the environment tree

### Understand

`infrastructure/modules/` is environment-agnostic — every `dev` mention there is inside a
`description` field, never a value. `infrastructure/environments/dev/` is the opposite: it is
*supposed* to hold environment-specific values, and it contains roughly sixty of them.

So this phase is a careful edit, not a rename. The list below is exhaustive as of commit `1cdfa11`.

### Execute — (a) copy the tree

```powershell
robocopy "infrastructure\environments\dev" "infrastructure\environments\$ENV" /E `
  /XD ".terragrunt-cache" ".terraform" `
  /XF "*.tfplan" "backend.tf" "provider.tf" "versions.tf" `
  /NFL /NDL /NJH /NJS /NP

Write-Host "robocopy exit code: $LASTEXITCODE"
```

> **Expect a non-zero exit code.** Robocopy uses exit codes as a bitmask: 1 means files
> were copied successfully. Anything under 8 is success; 8 or above is a real failure.
>
> **Why robocopy and not `Copy-Item` + delete.** `.terragrunt-cache` holds downloaded
> provider binaries, and Windows locks them while any process has them open. Copying
> everything then deleting fails partway and leaves a half-cleaned tree. Excluding at
> copy time avoids the problem instead of cleaning up after it.

> **Keep `.terraform.lock.hcl`.** These pin exact provider versions and checksums, so
> the new environment resolves the same providers as dev. Do not delete and regenerate
> them — `init` would fetch whatever is current, and the two environments would diverge
> silently. Same failure mode as installing Argo CD from a floating `stable` tag.

`backend.tf`, `provider.tf` and `versions.tf` are **generated** by Terragrunt from `root.hcl` at init
time. Excluding the copies guarantees they are regenerated against the new backend rather than
silently inherited.

### Execute — (b) the first edit, before anything else

Open `$ENVDIR/root.hcl` and change **line 31**:

```hcl
state_container = "tfstate-qa"     # was "tfstate"
```

Then **line 138**:

```hcl
inputs = {
  environment = "qa"               # was "dev"
}
```

Do this before any other edit. If you are interrupted, the environment is already safe.

> **Verify before continuing — do not assume this edit was applied:**
>
> ```powershell
> Select-String -Path "$ENVDIR/root.hcl" -Pattern "state_container|environment\s*="
> ```
>
> Must show `tfstate-<env>` and `environment = "<env>"`. This was missed once during a
> real build and only caught because a later step re-read the file. An unedited
> `root.hcl` points the new environment at dev's state — see Phase 0.

### Execute — (c) the remaining edits

Find every remaining `dev` value:

```powershell
Get-ChildItem -Path $ENVDIR -Recurse -Filter "terragrunt.hcl" |
  Select-String -Pattern "dev" |
  Where-Object { $_.Line -notmatch "^\s*#" } |
  Select-Object Path, LineNumber, Line
```

Work through them by category:

| Category | Appears in | Change |
|---|---|---|
| Resource group name `rg-antkart-dev-eastus` | 13 units | `rg-antkart-$ENV-eastus` |
| Resource names | one per unit (see table below) | swap `dev` → `$ENV` |
| `environment = "dev"` tag | every unit, plus `workload-identity` line 166 | `"qa"` |
| Mock-output resource IDs | `aks`, `role-assignments`, `workload-identity`, `github-oidc`, `governance` | swap `dev` → `$ENV` in the paths |
| GitHub OIDC subject claim | `github-oidc` line 70 | `{ name = "env-qa", claim = "environment:qa" }` |
| App registration display name | `app-registration` line 27 | `antkart-api-$ENV` |
| Key Vault purge protection | `key-vault` line 65 | `false` (Decision C) |
| Bare `"dev"` in tag arrays | `app-registration` | `["antkart", "dev"]` -> `["antkart", "<env>"]` |

> A resource-name pattern search will not catch a standalone `"dev"` inside a tag,
> list, or map value. Search for the bare quoted string separately.

Resource names by unit:

| Unit | dev value | New value |
|---|---|---|
| `resource-group` | `rg-antkart-dev-eastus` | `rg-antkart-$ENV-eastus` |
| `networking` | `vnet-antkart-dev-eastus` | `vnet-antkart-$ENV-eastus` |
| `aks` | `aks-antkart-dev` | `aks-antkart-$ENV` |
| `container-registry` | `acrantkartdev` | `acrantkart$ENV` |
| `key-vault` | `kv-antkart-dev` | `kv-antkart-$ENV` |
| `cosmosdb` | `cosmos-antkart-dev` | `cosmos-antkart-$ENV` |
| `servicebus` | `sb-antkart-dev` | `sb-antkart-$ENV` |
| `eventgrid` | `evgt-antkart-dev` | `evgt-antkart-$ENV` |
| `redis` | `redis-antkart-dev` | `redis-antkart-$ENV` |
| `observability` | `log-antkart-dev`, `appi-antkart-dev` | `log-antkart-$ENV`, `appi-antkart-$ENV` |
| `postgresql` | `psql-antkart-dev-eus2` | `psql-antkart-$ENV-eus2` |
| `function-app` | `func-antkart-notifications-dev`, `stantkartfuncdev` | `...-$ENV`, `stantkartfunc$ENV` |
| `communication-services` | `antkart-dev` prefix | `antkart-$ENV` |
| `governance` | `budget-antkart-dev` | `budget-antkart-$ENV` |
| `app-registration` | `antkart-api-dev` | `antkart-api-$ENV` |

> **Mock outputs.** The `00000000-0000-0000-0000-000000000000` subscription IDs are placeholders
> Terragrunt uses when a dependency has not been applied yet, so `plan` can run against an
> unbuilt graph. They are never real values — but the *resource names* inside them must still be
> updated, or a plan will validate against dev-shaped paths.
>
> A `dependency` block reads another unit's outputs. Terragrunt applies dependencies
> first and passes real values down; the mock values only stand in for a `plan` run
> before that unit exists.

### Verify

```powershell
# No unintended 'dev' values remain outside comments
Get-ChildItem -Path $ENVDIR -Recurse -Filter "terragrunt.hcl" |
  Select-String -Pattern "dev" |
  Where-Object { $_.Line -notmatch "^\s*#" }

# State container is correct — this is the one that matters
Select-String -Path "$ENVDIR/root.hcl" -Pattern "state_container|environment ="
```

**Correct result:** the first command returns nothing (or only intentional matches such as
`psql-...-eus2`, which contains no `dev`). The second shows `tfstate-qa` and `environment = "qa"`.

---

## Phase 2 — Waves 0 and 1 (foundation and standalone services)

### Understand

The 18 units form a dependency graph. Applying in wave order means every dependency's real outputs
exist before a dependent unit reads them.

| Wave | Units | Depends on |
|---|---|---|
| 0 | `resource-group`, `app-registration` | nothing |
| 1 | `networking`, `observability`, `container-registry`, `cosmosdb`, `postgresql`, `redis`, `servicebus`, `eventgrid`, `communication-services`, `governance` | `resource-group` |
| 2 | `aks`, `key-vault`, `github-oidc`, `function-app` | wave 0–1 |
| 3 | `workload-identity`, `role-assignments` | wave 2 |

### Execute — Wave 0

> `init` downloads the provider and, on first run, generates `backend.tf` and
> `provider.tf` from `root.hcl` and creates this unit's state blob.

```powershell
cd "$ENVDIR/resource-group"
terragrunt init
terragrunt plan
terragrunt apply

cd "../app-registration"
terragrunt init
terragrunt plan
terragrunt apply
```

**Before the first apply, read the plan output for the backend line.** It must reference
`tfstate-qa`. If it says `tfstate`, stop — Phase 1(b) did not take effect.

### Verify — Wave 0

```powershell
az group show --name $RG --query "{name:name, location:location, state:properties.provisioningState}" -o table
az ad app list --display-name $APPREG --query "[].{name:displayName, appId:appId}" -o table

# The state blob landed in the right container
az storage blob list --container-name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table
```

**Correct result:** resource group `Succeeded`; app registration listed; the new container now holds
`resource-group/terraform.tfstate` and `app-registration/terraform.tfstate`.

### Execute — Wave 1

> **Check the budget start date before applying `governance`.** The unit hardcodes
> `start_date`, and Azure rejects a monthly budget starting before the current month:
>
> `400: Start date for monthly time grain should not be prior to current month.`
>
> Set it to the first day of the current month in
> `$ENVDIR/governance/terragrunt.hcl`. Note that the source environment carries the same
> hardcoded date, so it can no longer be rebuilt from its own code either — worth an ADR
> to derive the date dynamically with a `lifecycle { ignore_changes }` guard.

Plan every unit first, then apply. `--terragrunt-include-dir` was renamed in newer
Terragrunt releases and this repo pins no version, so the loop below avoids the flag
entirely:

```powershell
$wave1 = @("networking","observability","container-registry","cosmosdb","postgresql",
           "redis","servicebus","eventgrid","communication-services","governance")

# Pass 1 — plan everything, change nothing
foreach ($u in $wave1) {
  Write-Host "=== plan: $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"; terragrunt init; terragrunt plan
}
cd $ENVDIR
```

Then apply unit by unit. Applying individually on a first build means a failure names its own unit:

```powershell
foreach ($u in @("networking","observability","container-registry","cosmosdb","postgresql",
                 "redis","servicebus","eventgrid","communication-services","governance")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

> PostgreSQL and Cosmos DB are the slow ones — allow 10–15 minutes each.

### Verify — Wave 1

```powershell
# Everything created in the resource group
az resource list --resource-group $RG --query "[].{name:name, type:type, location:location}" -o table

# Individually
az network vnet show -g $RG -n $VNET --query "{name:name, subnets:length(subnets)}" -o table
az acr show -n $ACR --query "{name:name, sku:sku.name, login:loginServer}" -o table
az cosmosdb show -g $RG -n $COSMOS --query "{name:name, kind:kind, state:provisioningState}" -o table
az postgres flexible-server show -g $RG -n $PG --query "{name:name, state:state, version:version}" -o table
az servicebus namespace show -g $RG -n $SB --query "{name:name, sku:sku.name, state:status}" -o table
az eventgrid topic show -g $RG -n $EVGT --query "{name:name, state:provisioningState}" -o table
# Azure Managed Redis (azurerm_managed_redis) is NOT Azure Cache for Redis —
# `az redis show` targets the wrong provider. Look it up generically by name.
az resource list -g $RG --name $REDIS --query "[].{name:name, type:type, location:location}" -o table
az monitor log-analytics workspace show -g $RG -n $LOG --query "{name:name, sku:sku.name}" -o table
az monitor app-insights component show -g $RG -a $APPI --query "{name:name, appId:appId}" -o table
```

**Correct result:** the VNet reports **3** subnets (`aks`, `private-endpoints`, `gateway`); every
resource reports a succeeded/active state.

**Immediately stop PostgreSQL if the next wave is not starting now:**

```powershell
az postgres flexible-server stop -g $RG -n $PG
az postgres flexible-server show -g $RG -n $PG --query "state" -o tsv
```

---

## Phase 3 — Waves 2 and 3 (cluster, vault, identity)

### Understand

Wave 2 creates the cluster and the vault. Wave 3 creates the federated identities that let pods
reach the vault without secrets — and it can only run after the cluster exists, because a federated
credential is issued against the **cluster's OIDC issuer URL**, which does not exist until the
cluster does.

That ordering constraint is already encoded: `workload-identity` declares a dependency on `aks`, so
Terragrunt sequences it for you. This is worth understanding rather than trusting — it is the single
most interview-relevant piece of the build.

> This is federated identity: no secret is stored anywhere. The pod presents a token
> signed by the cluster, Entra trusts that specific issuer and subject, and hands back
> an Azure token. Change the ServiceAccount name and the trust no longer matches.

### Execute — Wave 2

```powershell
foreach ($u in @("aks","key-vault","github-oidc","function-app")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt init
  terragrunt plan
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

> **Check the Key Vault plan before approving.** Confirm `purge_protection_enabled = false`
> (Decision C). This is a one-way door — after apply it cannot be changed.

AKS takes 10–15 minutes.

### Verify — Wave 2

```powershell
# Cluster running, and its OIDC issuer exists (wave 3 depends on this)
az aks show -g $RG -n $AKS --query "{name:name, power:powerState.code, k8s:kubernetesVersion, oidc:oidcIssuerProfile.enabled, wi:securityProfile.workloadIdentity.enabled}" -o table
az aks show -g $RG -n $AKS --query "oidcIssuerProfile.issuerUrl" -o tsv

# Vault, and purge protection is off
az keyvault show -n $KV --query "{name:name, rbac:properties.enableRbacAuthorization, purge:properties.enablePurgeProtection, softDelete:properties.softDeleteRetentionInDays}" -o table

# The cluster can pull from the registry
az aks check-acr -g $RG -n $AKS --acr "$ACR.azurecr.io"

# Function app
az functionapp show -g $RG -n $FUNC --query "{name:name, state:state, https:httpsOnly}" -o table
```

**Correct result:** `powerState = Running`, OIDC issuer enabled with a URL, workload identity
enabled, vault `enablePurgeProtection` **null or false**, and `az aks check-acr` reporting success.

> When purge protection is off, `enablePurgeProtection` returns **null**, which prints as a
> blank column rather than `false`. A blank value is the correct result — Azure only ever
> stores `true` or null for this property.

> `check-acr` writes a temporary file locally and can fail with
> `Permission denied when trying to write to ...\Temp\...` — a local filesystem problem
> (antivirus, sync client, folder permissions), not an Azure one. Pass `-f <writable-path>`,
> or verify the underlying fact directly:
>
> ```powershell
> az role assignment list --scope $(az acr show -n $ACR --query id -o tsv) `
>   --query "[].{principal:principalName, role:roleDefinitionName}" -o table
> ```
>
> Expect **AcrPull** (the AKS kubelet identity) and **AcrPush** (the CI/CD identity).

### Execute — Wave 3

```powershell
foreach ($u in @("workload-identity","role-assignments")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt init
  terragrunt plan
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

> Role assignments can fail on first apply with a principal-not-found error when the managed
> identity was created seconds earlier and is not yet visible to the RBAC service. This is
> transient — wait a minute and re-run the unit.

### Verify — Wave 3

```powershell
# Six managed identities, one per service
az identity list -g $RG --query "[].{name:name, clientId:clientId}" -o table

# Federated credentials point at THIS cluster's issuer
$ISSUER = az aks show -g $RG -n $AKS --query "oidcIssuerProfile.issuerUrl" -o tsv
foreach ($mi in (az identity list -g $RG --query "[].name" -o tsv)) {
  Write-Host "--- $mi ---"
  az identity federated-credential list -g $RG --identity-name $mi `
    --query "[].{name:name, subject:subject, issuer:issuer}" -o table
}
Write-Host "Cluster issuer: $ISSUER"

# Role assignments landed on the vault, bus and topic
az role assignment list --scope $(az keyvault show -n $KV --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
az role assignment list --scope $(az servicebus namespace show -g $RG -n $SB --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
az role assignment list --scope $(az eventgrid topic show -g $RG -n $EVGT --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
```

**Correct result:** **seven** identities — the six services (`gateway`, `products`, `cart`, `order`,
`payments`, `discount`) plus a CI/CD identity `id-ak-cicd-<env>` (which holds **AcrPush** on the
registry); every federated credential's `issuer` matches the cluster issuer URL exactly; subjects read
`system:serviceaccount:antkart:ak-<service>`.

> **Record the subjects now — Phase 5 depends on them.** Each federated credential's subject reads
> `system:serviceaccount:<namespace>:ak-<service>`. The Kubernetes ServiceAccount created in Phase 5.2
> must match character for character. A mismatch produces a token-exchange failure that reads as an
> authentication problem and is actually a naming problem.
>
> Also record the cluster's OIDC issuer URL and each identity's client ID — Phase 5.2 needs the
> client IDs for ServiceAccount annotations.

The Key Vault scope shows **eight** role assignments: the six service identities with Key Vault
Secrets User, the Function App's identity with Secrets User, and the Terraform service principal with
Secrets Officer.

Expected role assignments:

| Identity | Key Vault | Service Bus | Event Grid |
|---|---|---|---|
| `ak-gateway` | Secrets User | — | — |
| `ak-products` | Secrets User | Data Sender + Receiver | — |
| `ak-cart` | Secrets User | Data Sender + Receiver | — |
| `ak-order` | Secrets User | Data Sender + Receiver | Data Sender |
| `ak-payments` | Secrets User | Data Sender + Receiver | Data Sender |
| `ak-discount` | Secrets User | — | — |

---

## Phase 4 — Post-provision: secrets and connectivity

### Understand

Terraform provisions the vault but does not seed application secrets — connection strings are read
from the resources *after* they exist. Nothing in the platform stores a secret in Git; each service
reads from Key Vault at startup via `DefaultAzureCredential`.

> **Do not print secret values to a terminal.** Commands such as `az cosmosdb keys list` return live
> credentials to stdout, where they persist in scrollback and in any log or transcript. Capture into a
> variable and print lengths. If a credential is exposed, rotate it — for Cosmos:
> `az cosmosdb keys regenerate --name $COSMOS -g $RG --key-kind primary` (after seeding, or the seeded
> value is invalidated).

### Execute

The sequence below is what seeded the QA vault end to end. **Never print a value** (see the note
above) — gather into variables, check lengths, then seed. The authoritative connection-string formats
are in [Operations Command Reference](operations-command-reference.md).

**Step 1 — start PostgreSQL.** Its connection strings cannot be built or verified while the server is
stopped:

```powershell
az postgres flexible-server start -g $RG -n $PG
```

Stop it again after Phase 4 (see Phase 7 → Daily stop), or it bills silently.

**Step 2 — gather values into variables (do not print them).**

```powershell
# PostgreSQL admin password — from the postgresql unit's Terraform output
Push-Location "$ENVDIR/postgresql"; $pgPass = terragrunt output -raw administrator_password; Pop-Location

# PostgreSQL host
$pgHost = az postgres flexible-server show -g $RG -n $PG --query fullyQualifiedDomainName -o tsv

# Cosmos (Mongo API) — the connection string contains '&' (see the --file warning below)
$cosmos = az cosmosdb keys list --name $COSMOS -g $RG --type connection-strings `
  --query "connectionStrings[0].connectionString" -o tsv

# Redis — preferred source is the redis unit's Terraform output
Push-Location "$ENVDIR/redis"; $redis = terragrunt output -raw connection_string; Pop-Location
```

> **Redis keys are not on the cluster resource.** `az resource invoke-action ... --action listKeys`
> returns `Not Found` for Azure Managed Redis — the keys live on the `databases/default` sub-resource,
> not the cluster. Prefer the Terraform `connection_string` output above; the REST fallback (Terraform
> output remains the preferred source) is:
>
> ```powershell
> az rest --method POST `
>   --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/$RG/providers/Microsoft.Cache/redisEnterprise/$REDIS/databases/default/listKeys?api-version=2024-09-01-preview"
> ```

Build the four PostgreSQL connection strings — one per database, differing only by the database name:

| Secret | Database (consumer) |
|---|---|
| `ConnectionStrings--Postgres` | `AKOrdersDb` (AK.Order; AK.Notification falls back to it) |
| `ConnectionStrings--PaymentsDb` | `AKPaymentsDb` (AK.Payments) |
| `ConnectionStrings--DiscountDb` | `AKDiscountDb` (AK.Discount) |
| `ConnectionStrings--Notifications` | `AKNotificationsDb` (AK.Notification) |

Each uses the format `Host=<fqdn>;Port=5432;Database=<db>;Username=antkartadmin;Password=<pw>;SslMode=Require`:

```powershell
$pgFmt = "Host=$pgHost;Port=5432;Database={0};Username=antkartadmin;Password=$pgPass;SslMode=Require"
$orders        = $pgFmt -f "AKOrdersDb"
$payments      = $pgFmt -f "AKPaymentsDb"
$discount      = $pgFmt -f "AKDiscountDb"
$notifications = $pgFmt -f "AKNotificationsDb"
```

The other two Azure-derived secrets are `MongoDbSettings--ConnectionString` (`$cosmos`, AK.Products) and
`RedisSettings--ConnectionString` (`$redis`, AK.ShoppingCart).

**Step 3 — confirm every variable is populated by printing LENGTHS only, never values:**

```powershell
foreach ($p in ([ordered]@{ pgPass=$pgPass; pgHost=$pgHost; cosmos=$cosmos; redis=$redis;
                            orders=$orders; payments=$payments; discount=$discount; notifications=$notifications }).GetEnumerator()) {
  Write-Host ("{0,-14} len={1}" -f $p.Key, $p.Value.Length)
}
```

Any `len=0` means a value did not resolve — fix it before seeding.

> **Use `--file`, not `--value`, for any secret containing `&`.** On Windows the `az` command is a
> `.cmd` wrapper, and `cmd` treats `&` as a command separator. A Cosmos DB Mongo connection string
> contains four of them. Passing it with `--value` stores a silently TRUNCATED secret and tries to
> execute the remainder as commands:
>
> ```
> 'replicaSet' is not recognized as an internal or external command
> 'retrywrites' is not recognized as an internal or external command
> ```
>
> The stored value ends at the first `&`, losing `retrywrites=false` — which the Cosmos Mongo API
> requires. The failure appears at runtime, not at seeding time.
>
> Write the value to a file and pass `--file`:
>
> ```powershell
> $tmp = Join-Path $env:USERPROFILE "secret-tmp.txt"
> [System.IO.File]::WriteAllText($tmp, $value, [System.Text.UTF8Encoding]::new($false))
> az keyvault secret set --vault-name $KV --name "<name>" --file "$tmp" --output none
> Remove-Item $tmp -Force
> ```
>
> `WriteAllText` with a no-BOM encoder is required — `Out-File` prepends a byte-order mark that becomes
> part of the secret value.
>
> This is the third form of the same Windows wrapper problem in this runbook, alongside KQL quoting and
> JMESPath parentheses. When in doubt, prefer `--file` for every secret.

> **`$pid` and other automatic variables fail silently.** PowerShell reserves `$pid`, `$host`, `$args`,
> `$input`, `$error` and `$matches`. Assigning to one raises `Cannot overwrite variable PID because it
> is read-only or constant` — but in a loop the error scrolls past and the ORIGINAL value is used
> downstream. In this build that produced `The Principal ID '22548' is not valid` — 22548 being the
> shell's process id. Use descriptive names such as `$principalId`.

**Step 4 — seed the eight secrets.** Six built above from Azure resources, plus the two Razorpay keys
copied from the source environment (same sandbox account). Seed every secret through a no-BOM file so a
value containing `&` is never mangled:

```powershell
$secrets = [ordered]@{
  "ConnectionStrings--Postgres"       = $orders
  "ConnectionStrings--PaymentsDb"     = $payments
  "ConnectionStrings--DiscountDb"     = $discount
  "ConnectionStrings--Notifications"  = $notifications
  "MongoDbSettings--ConnectionString" = $cosmos
  "RedisSettings--ConnectionString"   = $redis
}
# Razorpay — copied from the source vault (same sandbox account), captured but never printed
foreach ($n in @("Razorpay--KeyId","Razorpay--KeySecret")) {
  $secrets[$n] = az keyvault secret show --vault-name "kv-antkart-dev" --name $n --query "value" -o tsv
}
foreach ($s in $secrets.GetEnumerator()) {
  $tmp = Join-Path $env:USERPROFILE "secret-tmp.txt"
  [System.IO.File]::WriteAllText($tmp, $s.Value, [System.Text.UTF8Encoding]::new($false))
  az keyvault secret set --vault-name $KV --name $s.Key --file "$tmp" --output none
  Remove-Item $tmp -Force
}
```

**Do NOT create** the two secrets the source vault still carries — `cosmos-connection-string` and
`servicebus-connection-string`. Neither is read by any service (Cosmos is reached via
`MongoDbSettings:ConnectionString`, Service Bus via `ServiceBus:FullyQualifiedNamespace` + workload
identity); they are dormant credentials from before the secret-less migration, tracked as
[KI-011](../KNOWN_ISSUES.md). QA seeds **eight** secrets, not ten.

**Step 5 — verify** the seeded secrets by length and name (next section).

### Verify

> **Verify by length and comparison, never by printing values.**
>
> ```powershell
> foreach ($n in @("ConnectionStrings--Postgres","ConnectionStrings--PaymentsDb",
>                  "ConnectionStrings--DiscountDb","ConnectionStrings--Notifications",
>                  "RedisSettings--ConnectionString","MongoDbSettings--ConnectionString")) {
>   $v = az keyvault secret show --vault-name $KV --name $n --query "value" -o tsv
>   Write-Host ("{0,-34} len={1}" -f $n, $v.Length)
> }
> ```
>
> The four PostgreSQL strings should differ only by the length of their database names — a useful
> independent check that nothing was truncated. For a secret copied from another environment, compare
> with `($dev -eq $qa)` rather than printing either value.
>
> To inspect a format safely, mask the credential:
> `$v -replace '(Password=)[^;]*','$1***' -replace '://[^@]*@','://***@'`
>
> Final check — compare secret NAMES between environments:
>
> ```powershell
> $srcSecrets = az keyvault secret list --vault-name "kv-antkart-dev" --query "[].name" -o tsv
> $newSecrets = az keyvault secret list --vault-name $KV --query "[].name" -o tsv
> Compare-Object $srcSecrets $newSecrets
> ```
>
> The only expected differences are `cosmos-connection-string` and `servicebus-connection-string` on
> the source side — the two with no consumer. Any other difference means a seed failed.

### Verify — cluster connectivity

```powershell
az aks get-credentials -g $RG -n $AKS --overwrite-existing
kubectl get nodes -o wide
kubectl get namespaces
```

**Correct result:** two nodes `Ready`.

---

## Phase 5 — GitOps and CD (session two)

> Takes a provisioned-but-empty cluster (end of Phase 4) to all six services running and reconciled
> by Argo CD. **Sections 5.1–5.7 and all of Phase 6 have now been run end to end against a real QA
> build** (which ended with the full Postman saga passing over HTTPS at `https://qa.antkart.in`);
> **sections 5.8 (CD promotion), 5.9 (Notification path), and the optional Phase 6A (API Management edge)
> remain `⚠️ UNVERIFIED`.** Each section follows the house pattern: **Understand → Execute → Verify → If it fails.**

### 5.0 Decisions to make before starting

No commands — decide these first. **5.3 onward cannot proceed until the promotion model is chosen**:
it determines where the new environment's Helm values live and what path every Argo CD Application
points at. Today there is exactly ONE values set under `deploy/helm/values/` (dev-targeted), so any
choice below is a new convention worth an ADR.

| Decision | Options | Consequence |
|---|---|---|
| **Promotion model** | (a) values per environment (`deploy/helm/values/<env>/<svc>.yaml`); (b) base + overlay (`values/base/` + `values/<env>/`); (c) separate GitOps repo per environment | (a) simplest, explicit, duplicates common keys; (b) DRY, more indirection; (c) cleanest isolation — **ADR-024 already names a separate repo as the long-term answer** |
| **App delivery object** | `applicationset-antkart.yaml` (**RECOMMENDED** in `deploy/argocd/README.md`) vs the six `applications/ak-*.yaml` | Apply **ONE, never both** — they create the same Application names |
| **Ingress controller** | reuse dev's ingress-nginx approach vs install fresh per environment | A shared controller = one public IP + one DNS story; a fresh per-env controller isolates blast radius but needs its own IP + DNS record |
| **CD trigger** | CD workflows target this environment vs deploy manually first | The `github-oidc` unit issues an `environment:<env>` claim (5.8); a workflow must declare that GitHub Environment for its OIDC token to match. Manual-first (`kubectl apply` the Applications) de-risks the first build |

### 5.1 Cluster prerequisites

**Understand.** Argo CD runs inside the cluster and reconciles Git (pull-based). Pin its version to
match dev — `stable` resolves to whatever is current at install time, which would give this
environment a different Argo CD than dev.

> **Why a pinned version and not `stable`.** The repo's other install docs use `argo-cd/stable`,
> which resolves to whatever is current at install time. The dev cluster runs v3.4.5 because that is
> what `stable` meant on the day it was installed. Using `stable` here would give this environment a
> different Argo CD than dev, so the version is pinned to match.

**Execute.**

```powershell
kubectl create namespace antkart
kubectl create namespace argocd

# Argo CD — match the dev version
kubectl apply --server-side -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/v3.4.5/manifests/install.yaml
kubectl -n argocd rollout status deploy/argocd-server --timeout=300s
```

> **Use `--server-side` from the start.** A plain `kubectl apply` stores the whole manifest in a
> `last-applied-configuration` annotation, and the ApplicationSet CRD exceeds the 262144-byte
> annotation limit:
> `The CustomResourceDefinition "applicationsets.argoproj.io" is invalid: metadata.annotations: Too long`
> Every other resource applies; only that CRD fails.
>
> Re-running with `--server-side` after a client-side apply produces ownership conflicts
> (`conflict with "kubectl-client-side-apply"`) because the two modes use different field managers.
> Adding `--force-conflicts` resolves it safely when the values are identical. Starting with
> `--server-side` avoids both.

**Verify.** Namespaces exist, argocd pods `Running`, and the server image matches the pinned version:

```powershell
kubectl get namespace antkart argocd
kubectl -n argocd get pods
kubectl -n argocd get deploy argocd-server -o jsonpath='{.spec.template.spec.containers[0].image}'
```

The image tag must read `v3.4.5`.

**If it fails.** A pod stuck `Pending` usually means the two-node cluster is out of schedulable
capacity — `kubectl -n argocd describe pod <name>` shows the scheduling reason.

### 5.2 Service accounts for workload identity

**Nothing to create — the chart templates it.** There is no manual `kubectl create serviceaccount`
step: `deploy/helm/antkart-service/templates/serviceaccount.yaml` renders one ServiceAccount per
service. It carries the label `azure.workload.identity/use: "true"` (which enables the
workload-identity mutating webhook) and the annotation `azure.workload.identity/client-id`, sourced
from `.Values.workloadIdentityClientId` — declared `required`, so a missing value **fails the render**
rather than producing a silently broken account. The account name comes from the values `name:` key,
which **must match** the federated credential subject `system:serviceaccount:<namespace>:ak-<service>`
created in Phase 3.

The only input to get right is therefore each service's `workloadIdentityClientId` (set in 5.3); list
the environment's identities to read the client IDs off:

```powershell
az identity list -g $RG --query "[].{name:name, clientId:clientId}" -o table
```

**Verify.** The rendered SA name must equal the federated credential subject
`system:serviceaccount:<namespace>:<sa-name>` created in Phase 3 (`workload-identity` unit). Compare
both sides:

```powershell
# The SAs the chart will create (namespace/name):
kubectl get serviceaccount -n antkart -o "custom-columns=NS:.metadata.namespace,NAME:.metadata.name"

# The federated subjects the identities actually trust:
foreach ($id in (az identity list -g $RG --query "[].name" -o tsv)) {
  az identity federated-credential list --identity-name $id -g $RG --query "[].{cred:name, subject:subject}" -o table
}
```

Each SA `antkart/ak-<service>` must have a matching subject `system:serviceaccount:antkart:ak-<service>`.

**If it fails.** A mismatch surfaces at runtime, not deploy time: the pod's token exchange is
rejected with an `AADSTS70021`-style "no matching federated identity record for the presented
assertion subject" error in the pod logs. The match is exact and case-sensitive — align the values
`name`/namespace with the federated-credential subject.

### 5.3 Helm values for the new environment

**Understand — three configuration layers, last wins.** Each service resolves configuration from three
sources, each overriding the one before:

1. **`appsettings.json`** (committed) — defaults, and **every one points at the source environment**
   (e.g. `KeyVault:Uri = https://kv-antkart-dev.vault.azure.net/`).
2. **Helm `env:` values** — per-deployment overrides rendered into a ConfigMap and injected as
   environment variables (.NET maps `__` → `:`), from `deploy/helm/antkart-service/values.yaml` (shared
   defaults) plus `deploy/helm/values/<svc>.yaml` (per service).
3. **Key Vault** — secrets loaded at startup, located by **`KeyVault:Uri`** as resolved from layers 1–2.

**⚠️ THE CRITICAL GAP — `KeyVault:Uri`.** It is set to the **source** vault
(`https://kv-antkart-dev.vault.azure.net/`) in every service's `appsettings.json`
(`AK.Products/AK.Products.API/appsettings.json`, `AK.ShoppingCart/AK.ShoppingCart.API/appsettings.json`,
`AK.Order/AK.Order.API/appsettings.json`, `AK.Payments/AK.Payments.API/appsettings.json`,
`AK.Discount/AK.Discount.Grpc/appsettings.json`) **and** in the chart's shared default
`deploy/helm/antkart-service/values.yaml` (`env.KeyVault__Uri`) — both `kv-antkart-dev`. **No file in
`deploy/helm/values/` overrides it.** A new environment MUST set `KeyVault__Uri` to its own vault, or
every pod starts, reads the **wrong (source) vault**, presents its **own** (new-environment) identity,
receives **403** (that identity holds no data-plane role on the source vault), and **crash-loops with
an error naming a vault the operator never built** (see Appendix B).

**Per-service override checklist.** Each key below carries a `dev` value in the repo; change it for the
new environment. Files cited are where the key lives.

| Service | Values file | Keys to set for the new environment |
|---|---|---|
| _shared_ | `deploy/helm/antkart-service/values.yaml` | `image.registry` (`acrantkartdev.azurecr.io`) · `env.KeyVault__Uri` (`https://kv-antkart-dev.vault.azure.net/`) — the two shared defaults, both `dev` |
| products | `deploy/helm/values/products.yaml` | `workloadIdentityClientId` · `env.ServiceBus__FullyQualifiedNamespace` · `env.Entra__Audience` · `env.Entra__ClientId` · `image.tag` |
| cart | `deploy/helm/values/cart.yaml` | `workloadIdentityClientId` · `env.ServiceBus__FullyQualifiedNamespace` · `env.Entra__Audience` · `env.Entra__ClientId` · `image.tag` |
| order | `deploy/helm/values/order.yaml` | `workloadIdentityClientId` · `env.ServiceBus__FullyQualifiedNamespace` · `env.EventGrid__TopicEndpoint` · `env.Entra__Audience` · `env.Entra__ClientId` · `image.tag` |
| payments | `deploy/helm/values/payments.yaml` | `workloadIdentityClientId` · `env.ServiceBus__FullyQualifiedNamespace` · `env.EventGrid__TopicEndpoint` · `env.Entra__Audience` · `env.Entra__ClientId` · `image.tag` |
| discount | `deploy/helm/values/discount.yaml` | `workloadIdentityClientId` · `image.tag` — injects **no** `Entra__*` and **no** `ServiceBus__*` |
| gateway | `deploy/helm/values/gateway.yaml` | `workloadIdentityClientId` · `env.Entra__Audience` · `env.Entra__ClientId` · `image.tag` · `ingress.host` (via the Argo parameter — see 5.5) |

Notes on the table:

- **`KeyVault__Uri`** — override once in the shared `deploy/helm/antkart-service/values.yaml` (it reaches
  every pod), or per service. It is the single most important override (the critical gap above).
- **`image.tag`** — set an initial real tag; CD then owns it (it bumps the tag in Git per commit).
- **`env.Entra__TenantId`** stays the **same** across environments in this single-tenant setup
  (`4cacc56a-…`, in each service's values), while **`env.Entra__Audience`** and **`env.Entra__ClientId`**
  are **per-environment** — each environment has its own app registration (`api://antkart-api-<env>` and
  that app's client id).
- Leave unchanged: `name`/`serviceName` (in-cluster identity), `env.Entra__Instance`, the in-cluster DNS
  overrides (`DiscountGrpc__Address`, `ProductsApi__BaseUrl`), `RedisSettings__*`, `MongoDbSettings__*`,
  `image.name` (cart→`shoppingcart`).

> **Connection strings and API keys are NOT values.** `ConnectionStrings:*`, `RedisSettings:ConnectionString`,
> the Razorpay keys and the App Insights connection string are Key Vault **secrets**, seeded in Phase 4 /
> 5.6 — never Helm values.

> **`image.registry`** is set only in `deploy/helm/antkart-service/values.yaml` and is overridden by no
> per-service values file. A new environment must add it to each of its six files, or pods pull from
> the source registry — for which the new cluster's kubelet has no AcrPull, producing
> `ImagePullBackOff`. Do not change the chart default; that would repoint the source environment on its
> next sync.

> **`ingress.host` and `ingress.clusterIssuer`** live as Helm `parameters` on the gateway's Argo
> Application, not in the values file. Copying that Application unchanged points the new environment at
> the source environment's public hostname and requests a production certificate for it — two clusters
> serving one host, and a risk of exhausting the Let's Encrypt limit of 5 duplicate certificates per
> hostname per 168 hours on the domain that matters. Set the new environment's own subdomain and
> `letsencrypt-staging`.

**Verify — grep the new values for the source environment's name.** Any hit is a missed override:

```powershell
Select-String -Path "<new-env values path>\*.yaml" -Pattern "antkart-dev|kv-antkart-dev|api://antkart-api-dev"
```

Then `helm template` each service renders without error:

```powershell
helm template ak-products deploy/helm/antkart-service -f <new-env values path>\products.yaml
```

**If it fails.** `Error: … values.workloadIdentityClientId is required` (from `serviceaccount.yaml`)
means that key is missing. A pod crash-looping with a **403 on `kv-antkart-dev`** means `KeyVault__Uri`
was not overridden — the critical gap above.

> **Argo can report `Synced` while pods run stale configuration.**
>
> The chart renders `.Values.env` into a ConfigMap (`templates/configmap-env.yaml`) and the Deployment
> consumes it with `envFrom.configMapRef`. Changing a value in Git therefore changes the ConfigMap —
> but leaves the Deployment's pod template byte-identical. Kubernetes has no reason to roll the pods,
> so they keep the values they read at startup.
>
> The symptom is confusing because everything reports healthy:
> - `kubectl get application` shows `Synced` / `Healthy`
> - `.status.sync.revision` matches the Git HEAD exactly
> - the ConfigMap holds the NEW value
> - `kubectl exec ... printenv` inside the pod shows the OLD value
>
> Diagnose by comparing the two directly:
> ```powershell
> kubectl get configmap <svc>-env -n antkart -o jsonpath="{.data.<KEY>}"
> kubectl exec -n antkart deploy/<svc> -- printenv | Select-String "<KEY>"
> ```
> A mismatch confirms it. Force the roll:
> ```powershell
> kubectl rollout restart deploy/<svc> -n antkart
> ```
>
> **The chart-level fix** is a checksum annotation on the pod template, which makes the template change
> whenever the ConfigMap does:
> ```yaml
> annotations:
>   checksum/config: {{ include (print $.Template.BasePath "/configmap-env.yaml") . | sha256sum }}
> ```
> Until that is added, every environment has silent config drift that reports healthy.

### 5.4 Container registry — seed images and confirm access

> A new environment's registry is empty, and CD does not target it yet (5.8). Import the images the
> source environment is running, so the new environment runs identical artefacts — same digests, not a
> rebuild:
>
> ```powershell
> foreach ($repo in @("gateway","products","shoppingcart","order","payments","discount")) {
>   az acr import --name $ACR `
>     --source "acrantkartdev.azurecr.io/antkart/${repo}:<tag>" `
>     --image "antkart/${repo}:<tag>" --force
> }
> az acr repository list --name $ACR -o table
> ```
>
> `az acr import` copies registry-to-registry server-side; nothing is pulled locally. Find the running
> tag with:
> `kubectl get pods -n antkart -o jsonpath="{.items[*].spec.containers[*].image}"`

**Understand.** The kubelet pulls images from the environment's ACR (`image.registry` =
`acrantkart<env>.azurecr.io`). AKS authenticates by its kubelet identity holding **AcrPull** on the
registry. If the cluster and ACR were provisioned by the same environment's Terraform this is already
wired, but a new environment must confirm it.

**Execute / Verify.**

```powershell
az aks check-acr --name $AKS --resource-group $RG --acr "acrantkart$ENV.azurecr.io"
```

**If it fails.** Grant AcrPull to the cluster's kubelet identity:

```powershell
$ACR_ID  = az acr show -n "acrantkart$ENV" --query id -o tsv
$KUBELET = az aks show -g $RG -n $AKS --query "identityProfile.kubeletidentity.objectId" -o tsv
az role assignment create --assignee-object-id $KUBELET --assignee-principal-type ServicePrincipal --role AcrPull --scope $ACR_ID
```

Symptom if skipped: pods stay `ImagePullBackOff` with a 401/403 from the registry.

### 5.5 Argo CD project and applications

**Understand.** Three manifests in `deploy/argocd/`: `appproject-antkart.yaml` (the least-privilege
AppProject — **apply FIRST**), then EITHER `applicationset-antkart.yaml` (**RECOMMENDED** — templates
all six from one `elements` list) OR the six `applications/ak-*.yaml` (the alternative). **Apply one,
never both** — they create the same Application names. Each Application (or ApplicationSet element)
points at the chart (`path: deploy/helm/antkart-service`) with `valueFiles: [ ../values/<svc>.yaml ]`;
the gateway additionally sets `ingress.enabled=true`, `ingress.host=api.antkart.in`,
`ingress.clusterIssuer=letsencrypt-prod` as Helm parameters.

> **The recurring trap.** Argo CD watches the chart and values an Application *points at*. It does
> **not** watch the Application manifest or the AppProject. Editing those in Git changes nothing
> until you `kubectl apply` them. This has caused real incidents on this platform. It is also why
> ADR-024 recommends a separate GitOps repository.

**Point it at the new environment.** Change the `valueFiles` path in each `applications/ak-*.yaml`
(or the ApplicationSet's `valueFile` per element) to the new environment's values path chosen in 5.0,
and the gateway's `ingress.host` parameter to the new hostname. If you keep both files, keep the
ApplicationSet element list and `applications/ak-gateway.yaml` in sync — but still apply only one.

**Execute.**

```powershell
kubectl apply -f deploy/argocd/appproject-antkart.yaml
# choose ONE of the next two — never both:
kubectl apply -f deploy/argocd/applicationset-antkart.yaml
# kubectl apply -f deploy/argocd/applications/
```

**Verify.**

```powershell
kubectl get applications -n argocd
```

All six show `Synced` / `Healthy`.

**If it fails.** Argo CD does **not** auto-retry a failed sync for the same Git revision — sync it
manually (`argocd app sync <name>` or the UI) after fixing the cause. An `OutOfSync`/`Degraded` app
on a first build is almost always missing secrets (5.6) → `CrashLoopBackOff`.

> **`Bus start faulted` with a 401 on `CreateOrUpdateSubscription`.**
>
> MassTransit reconciles its Service Bus topology at startup — creating or updating subscriptions and
> rules — which is a MANAGEMENT-plane operation. `Azure Service Bus Data Sender` and `Data Receiver`
> are data-plane roles and do not permit it, so the bus faults even when Terraform has already created
> the subscriptions:
>
> ```
> Status: 401 (SubCode=40100: Unauthorized : Unauthorized access for
> 'CreateOrUpdateSubscription' operation on endpoint
> sb://<ns>.servicebus.windows.net/integration-events/Subscriptions/<svc>)
> ```
>
> The failure is a WARNING, not a crash — pods stay `Running` and `Healthy` while messaging is broken.
> It will not show up in `kubectl get pods`.
>
> Grant `Azure Service Bus Data Owner` at the namespace scope to the identities that use the bus
> (products, cart, order, payments — gateway and discount have no ServiceBus configuration):
>
> ```powershell
> $sbScope = az servicebus namespace show -g $RG -n $SB --query id -o tsv
> foreach ($svc in @("products","cart","order","payments")) {
>   $principalId = az identity show -g $RG -n "id-ak-$svc-$ENV" --query principalId -o tsv
>   az role assignment create --assignee-object-id $principalId `
>     --assignee-principal-type ServicePrincipal `
>     --role "Azure Service Bus Data Owner" --scope $sbScope --output none
> }
> ```
>
> Note this uses **principalId**, not clientId — role assignments take the principal.
>
> Wait ~2 minutes, restart the four deployments, then confirm:
> ```powershell
> kubectl logs -n antkart deploy/ak-products --tail=50 | Select-String "Bus start|faulted|Unauthorized"
> ```
> No output is the pass.
>
> **ADR-worthy trade-off.** Data Owner supersedes Sender and Receiver and lets a compromised service
> reshape the messaging topology. The stricter alternative is to keep the least-privilege pair and
> pre-create every subscription and rule in Terraform, at the cost of MassTransit no longer managing
> its own topology. Record whichever is chosen and why.

### 5.6 Secret seeding

**Understand.** Pods cannot start without their Key Vault secrets — every service reads connection
strings / API keys from Key Vault at startup via `DefaultAzureCredential`. This is Phase 4's secret
step, called out here because ordering is load-bearing: **it must happen before Argo syncs**, or the
first sync produces `CrashLoopBackOff` as each pod fails to read its secrets. Seed the vault as in
[Phase 4 → Execute](#phase-4--post-provision-secrets-and-connectivity); the authoritative secret
names and formats live in [Operations Command Reference](operations-command-reference.md).

**Verify.** Compare secret **names** (never values) against the source environment:

```powershell
$devSecrets = az keyvault secret list --vault-name "kv-antkart-dev" --query "[].name" -o tsv
$newSecrets = az keyvault secret list --vault-name $KV --query "[].name" -o tsv
Compare-Object $devSecrets $newSecrets
```

`Compare-Object` returns nothing — the two vaults hold the same secret names.

**If it fails.** A pod in `CrashLoopBackOff` right after the first sync: `kubectl logs -n antkart
<pod>` shows a Key Vault / config-binding error naming the missing secret. Seed it, then let Argo
re-sync (or restart the deployment).

### 5.7 Ingress and cert-manager

**Understand.** The gateway is the only externally-exposed service, over HTTPS with a Let's Encrypt
certificate. Only the two `ClusterIssuer` manifests live in the repo —
`deploy/cert-manager/cluster-issuer-staging.yaml` (`letsencrypt-staging`) and
`deploy/cert-manager/cluster-issuer-prod.yaml` (`letsencrypt-prod`), both HTTP-01 over the `nginx`
ingress class. **Neither ingress-nginx nor cert-manager is captured in the repository** — both were
installed by hand in the source environment, so this runbook is now their only record. Version used:
cert-manager **v1.21.1**.

**Execute — each step gates the next.**

1. **Install ingress-nginx** via Helm into its own namespace, with
   `controller.service.externalTrafficPolicy=Local` (preserves the client source IP).
2. **Wait for the LoadBalancer `EXTERNAL-IP`** — Azure takes 1–3 minutes. Capture it and record it
   outside the terminal:

   ```powershell
   kubectl get svc ingress-nginx-controller -n ingress-nginx -o jsonpath="{.status.loadBalancer.ingress[0].ip}"
   ```

3. **Create the DNS A record** for the environment's subdomain pointing at that IP.
4. **Poll until DNS resolves** — this GATES certificate issuance, because HTTP-01 validation requires
   Let's Encrypt to fetch a file over `http://` at that hostname:

   ```powershell
   Resolve-DnsName <host>
   ```

5. **Install cert-manager** via Helm with `--set crds.enabled=true`, then wait for all three
   deployments — `cert-manager`, `cert-manager-webhook`, `cert-manager-cainjector`. The **webhook**
   matters most: applying a ClusterIssuer before it is ready gives a connection-refused error rather
   than a clear message.
6. **Apply both ClusterIssuers** from `deploy/cert-manager/`. `READY True` means cert-manager
   registered an ACME account:

   ```powershell
   kubectl apply -f deploy/cert-manager/cluster-issuer-staging.yaml
   kubectl apply -f deploy/cert-manager/cluster-issuer-prod.yaml
   kubectl get clusterissuer
   ```

7. **cert-manager issues automatically** from the Ingress annotation. Watch the chain top-down — the
   lowest object present shows where it stalled:

   ```powershell
   kubectl get certificate -n antkart
   kubectl get certificaterequest,order,challenge -n antkart
   ```

8. **Verify on staging** (untrusted root, so `-k`):

   ```powershell
   curl.exe -k -s -o NUL -w "%{http_code}" https://<host>/health/live
   ```

9. **Switch to production.** Edit the Argo Application's `clusterIssuer` parameter to `letsencrypt-prod`,
   commit, then `kubectl apply` it — **Argo does NOT watch its own Application manifest**. Delete the
   old TLS secret so cert-manager issues fresh. Verify **without** `-k` — a 200 can only come from a
   publicly trusted certificate:

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}" https://<host>/health/live
   ```

> **Start on staging** to debug the chain without spending the production rate limit — Let's Encrypt
> allows only **5 duplicate certificates per hostname per 168 hours**. Switch to `letsencrypt-prod`
> (step 9) only once staging returns `200`.

### 5.8 ⚠️ UNVERIFIED — CD workflows targeting this environment

**Understand.** The `github-oidc` unit issues a federated credential whose subject is
`repo:seesathish/AntKart-Src3:environment:<env>` (its `subjects` list carries
`{ name = "env-<env>", claim = "environment:<env>" }`). For a CD workflow's OIDC token to match, the
workflow job must declare that GitHub Environment. So the workflows need, for this environment:

- a job-level `environment: <env>` declaration, so the token carries `environment:<env>`;
- the new environment's values path for the image-tag bump step (the CD commit that updates
  `image.tag`), matching the promotion model from 5.0.

**Verify.** Confirm the federated credential subject exists on the app registration:

```powershell
az ad app federated-credential list --id $(az ad app list --display-name $APPREG --query "[0].id" -o tsv) `
  --query "[].{name:name, subject:subject}" -o table
```

Expect a row whose subject ends `environment:<env>`.

**If it fails.** A CD run failing at the Azure login step with a no-matching-federated-credential
error (`AADSTS700213`-style) means the workflow's `environment:` (or branch/subject) does not match
any credential subject — align the workflow declaration with the `github-oidc` subject.

### 5.9 ⚠️ UNVERIFIED — Notification path

> Terraform creates the Function App resource, but no code is published to it and its Event Grid
> subscription is not wired. A new environment therefore has a provisioned but non-functional
> notification path, and the end-to-end run in Phase 6 does not exercise it — the core saga completes
> without it.
>
> Three things are outstanding, none of them verified against a live environment:
>
> **Publish the Function code.** No deployment mechanism for the Notification Function is covered by
> this runbook. Determine during the build whether it is published by a CI/CD workflow, by
> `func azure functionapp publish`, or by another route, and record it here.
>
> **Wire the Event Grid subscription.** The Function subscribes to the environment's Event Grid topic.
> Confirm whether the subscription is created by Terraform, by the Function's own registration at
> startup, or manually.
>
> **Confirm the database schema.** Order, Payments and Discount apply EF Core migrations at startup via
> `MigrateAsync` in `Program.cs`. The Notification Function ships an `InitialCreate` migration but no
> startup migrate call was found in the repository, so `AKNotificationsDb` may have no schema. Verify
> before assuming the path works:
>
> ```powershell
> az functionapp show -g $RG -n $FUNC --query "{name:name, state:state}" -o table
> az eventgrid event-subscription list --source-resource-id $(az eventgrid topic show -g $RG -n $EVGT --query id -o tsv) --query "[].{name:name, endpoint:destination.endpointType}" -o table
> ```
>
> Until this section is verified, treat a new environment as core-saga-complete and
> notification-incomplete.

---

## Phase 6 — End-to-end verification

> The ordered sequence that took the QA environment to a green Postman run over HTTPS at
> `https://qa.antkart.in`. Each step has its own verification.

### 6.1 Smoke test with curl

Three curls prove routing, dependencies and the auth boundary before spending time on OAuth:

```powershell
curl.exe -s -o NUL -w "live:  %{http_code}`n" https://<host>/health/live
curl.exe -s -o NUL -w "ready: %{http_code}`n" https://<host>/health/ready
curl.exe -s -o NUL -w "no-token: %{http_code}`n" https://<host>/gateway/products
```

**Verify.** Expect 200, 200, and **401**. The 401 is a PASS — it proves Entra validation is active. A
200 on the last one would mean the gateway is not enforcing authentication.

**All pods healthy.**

```powershell
kubectl get pods -n antkart
```

Every pod `Running` with `READY 1/1` and no restarts. The curl smoke test only exercises the gateway;
a downstream service can be crash-looping while `/health/live` still answers. Check this before
spending time on OAuth.

**Workload identity is working.**

```powershell
kubectl logs -n antkart deploy/ak-products --tail=100 | Select-String "Key Vault configuration loaded"
```

Expect a line naming THIS environment's vault:
`Key Vault configuration loaded from "https://kv-antkart-<env>.vault.azure.net/"`

This single line proves the whole secret-less chain: the pod presented a token signed by the cluster's
OIDC issuer, Entra matched it to the federated credential created in Phase 3, and the resulting Azure
token was accepted by the vault's data plane. No secret is stored anywhere in the cluster.

If it names the SOURCE environment's vault instead, `KeyVault__Uri` was not overridden — see 5.3. If
the pod crash-loops with a 403, the identity is right but the role assignment is missing.

### 6.2 Confirm the environment's own configuration reached the pods

A `Synced` Application does not guarantee the running pods hold *this* environment's values — a
ConfigMap change alone does not roll the pods (see the callout after 5.3). Read the values from inside
a pod:

```powershell
kubectl exec -n antkart deploy/ak-gateway -- printenv | Select-String "Entra"
```

**Verify.** Expect the **new** environment's `Entra__Audience` and `Entra__ClientId`, not the source
environment's. A mismatch means the ConfigMap changed but the pods were not rolled —
`kubectl rollout restart deploy/ak-gateway -n antkart`.

### 6.3 Point Postman at the new environment

The collection `AntKart-Cloud-E2E-Saga-Positive.postman_collection.json` uses collection variables
`baseUrl` and `entraTenantId`, and expects two environment values `entraClientId` and
`razorpayKeySecret`. Three things must change for a new environment:

| Setting | Location | Change |
|---|---|---|
| `baseUrl` | collection variable | → `https://<host>` (the new gateway HTTPS URL) |
| `entraClientId` | environment value | the new environment's **public-client** app-registration id, not the API app id |
| **scope** | collection Authorization tab | `api://antkart-api-<env>/access_as_user openid profile offline_access` |
| `entraTenantId` | collection variable | unchanged — same tenant |

The **scope** is hardcoded in the auth configuration rather than templated, so it is the easiest to
miss. Leave it and Entra issues a token audienced for the source environment's API; the new gateway
rejects it with a 401 that looks like a broken login. Clear cookies and request a new token after
changing it — Postman caches tokens.

> Postman variables have separate **Initial** and **Current** values. Editing Initial alone changes
> nothing at runtime — Current is what requests use. Set both and save the collection. A request timing
> out rather than returning 401 usually means the old base URL is still in Current and points at a
> stopped environment.

### 6.4 Seed the catalogue

The products service seeds only when `Seeding__RunOnStartup` is `"true"`; a new environment's data
store is empty. Set it in the environment's values file, push, let Argo sync, then **restart the
deployment** — the ConfigMap change alone will not roll the pods (5.3):

```powershell
# after setting Seeding__RunOnStartup: "true" in the values file and letting Argo sync
kubectl rollout restart deploy/ak-products -n antkart
kubectl logs -n antkart deploy/ak-products --tail=100 | Select-String "seeded"
```

**Verify.** Expect `Database seeded with 300 sample products.` **Set `Seeding__RunOnStartup` back to
`"false"` and push once seeding has completed**, or every pod restart re-seeds.

### 6.5 Run the collection

Run with the **Collection Runner**, *Delay between requests* = 8000 ms (the saga is asynchronous and
steps 07/12 self-retry).

**Verify.** All 12 ordered requests pass and the order reaches `Paid` — the payment-success journey
end to end against the new environment through HTTPS.

### 6.6 Telemetry

```powershell
$WS = az monitor log-analytics workspace show -g $RG -n $LOG --query "customerId" -o tsv

# Traces spanning multiple services
az monitor log-analytics query --workspace $WS --analytics-query "union AppRequests, AppDependencies | where TimeGenerated > ago(60m) | summarize roles=make_set(AppRoleName), spans=count() by OperationId | where array_length(roles) > 1 | order by spans desc | take 5" -o table

# Structured logs carrying TraceId
az monitor log-analytics query --workspace $WS --analytics-query "ContainerLog | where TimeGenerated > ago(60m) | where LogEntry has 'TraceId' | project TimeGenerated, LogEntry | take 3" -o table
```

**Verify.** A healthy result: the first query returns at least one `OperationId` whose `roles` set
spans multiple services (e.g. `ak-gateway` + `ak-products` + `ak-order`) — proof a request was traced
across roles; the second returns structured log lines carrying a `TraceId`, correlatable back to that
`OperationId`.

> **Schema note.** `AppRequests`, `AppDependencies` and `TimeGenerated` are the **workspace** schema
> and require `az monitor log-analytics query`. The classic `az monitor app-insights query` uses
> `requests`, `dependencies` and `timestamp` — passing workspace table names to it returns
> `BadArgumentError: The request had some invalid properties`, which looks like a KQL problem and is
> not one.

---

## Phase 6A — Optional: API Management edge ⚠️ UNVERIFIED

Optional and ephemeral. This phase stands up the [ADR-020](../adr/ADR-020-api-management-managed-edge-gateway.md)
managed edge in front of an already-working environment, exercises it, and destroys it **in the same
session**. It is **not** part of a standard environment build — Phases 0-6 produce a complete, reachable
platform without it. Provision it only to prove the edge, then tear it down.

> **API Management does not behave like the rest of this platform.** Every other expensive resource here
> can be stopped between sessions — AKS and PostgreSQL both support stop and start, and Phase 7 relies on
> that. **API Management cannot be stopped.** Once created it bills continuously until it is deleted,
> regardless of use.
>
> Provisioning takes 30–45 minutes. Deletion takes a comparable time.
>
> Treat this phase as create, exercise, destroy — in one session. Do not provision it and come back to it
> next week.
>
> Check the current SKU pricing for the target region before creating anything; do not rely on a figure
> written in this document.

### 6A.0 Nine things this phase does not settle

> Phase 6A is written from ADR-020 and the target-state section of 6-security.md. Nine details are decided
> by neither. They are marked `→ confirm` where they occur; this list exists so they can be seen before
> anything is provisioned rather than discovered at minute thirty of a forty-five minute create.

| Gap | Why it is not settled | Blocks |
|---|---|---|
| **1 · Exact SKU CLI value & capacity** | ADR-020 decides *Developer tier, VNet-integrated*, but the precise `az apim create --sku-name` value/capacity and current pricing are not in the repo (and pricing must be checked live). | 6A.3 Create |
| **2 · A suitable VNet/subnet for APIM injection** | ADR-020 assumes VNet integration, but APIM injection has subnet-delegation + NSG requirements the networking unit does not provision. | 6A.2 (placement) → 6A.3 |
| **3 · How APIM reaches the cluster (public vs private)** | ADR-020 assumes *private* backends; today `ingress-nginx` is **public**. Fronting the public hostname vs making the ingress internal-only first is settled nowhere. | 6A.2 (and everything downstream) |
| **4 · DNS record strategy** | Move the env hostname to APIM vs give APIM its own hostname — not settled by ADR-020. | 6A.2 → 6A.5 (addressing) |
| **5 · Certificate handling at the APIM edge** | ADR-020 says TLS terminates at APIM but not *how* the cert is supplied (APIM managed cert / Key Vault cert / Let's Encrypt); the in-cluster cert-manager path (5.7) does not transfer. | 6A.4 Configure |
| **6 · The policy XML itself** | 6-security.md describes the chain but the repo commits no APIM policy XML. | 6A.4 Configure |
| **7 · `validate-jwt` OpenID metadata / policy shape** | Tenant and audience are known (§5.3), but the exact policy element/attribute shape is not in the repo. | 6A.4 Configure |
| **8 · Rate-limit/quota numbers & the product/subscription model** | Tiers, product names, and limit figures are specified nowhere concrete. | 6A.4 Configure |
| **9 · `az apim deletedservice` command forms** | The soft-delete list/purge commands are written from the KI-007 shape; the exact sub-command flags need confirming against current Azure docs. | 6A.6 Destroy |

> **Gap 3 is the one to resolve first.** ADR-020 assumes APIM reaches private backends, but the cluster's
> ingress is public today. Whether to front the existing public hostname or make the ingress internal-only
> first is a design decision, not a configuration detail — and making the ingress internal would break the
> verified environment until APIM is working. Settle it before provisioning anything.

### 6A.1 What this replaces, and what it does not

Per [ADR-020](../adr/ADR-020-api-management-managed-edge-gateway.md) and the **APIM edge (target state)**
section of [6-security.md](../development/6-security.md), APIM is a **two-gateway** model — an addition, not
a replacement:

- **What moves to the edge (APIM).** TLS termination, `validate-jwt` against Entra, rate-limit/quota,
  subscription keys / products, and request/response transformation become APIM policy at the public front
  door, so malformed or unauthenticated traffic is rejected before it reaches the cluster.
- **What the in-cluster gateway keeps doing.** `AK.Gateway` (Ocelot) remains the **routing** gateway behind
  the cluster's internal ingress — it routes each path to the service that owns it, exactly as before.
- **Defence-in-depth token validation is retained.** APIM validating the JWT does **not** remove in-service
  validation: `AK.Gateway` and each service **re-validate** audience, issuer, roles, and ownership. The edge
  is an outer wall and an optimization, never the trust boundary the services rely on (ADR-020).
- **The cluster works unchanged if APIM is absent.** Today traffic reaches `ingress-nginx` directly and the
  in-cluster gateway validates the token. Removing APIM at the end of this phase returns the environment to
  exactly that state.

### 6A.2 Decisions before creating

Settle these first — each has a consequence, and several are **not** fixed by ADR-020 (marked → confirm at
build time and listed in the PR description):

| Decision | Options / what ADR-020 says | Consequence |
|---|---|---|
| **SKU / tier** | ADR-020 selects **Developer tier, VNet-integrated**. The tier is the gate: it determines whether **VNet integration** and a **self-hosted gateway** are available at all — Consumption tier, for example, has neither. → confirm the exact `--sku-name` value and capacity against current Azure docs before running. | Developer has no production SLA and single-unit scale; adequate to prove the edge, not for production load (Premium is the planned step up). |
| **Placement — public or VNet-integrated** | ADR-020 assumes **VNet-integrated with private backends** — services reachable only from APIM and the internal ingress. | → confirm the existing environment actually has a VNet/subnet suitable for APIM injection; the networking unit provisions the cluster network but APIM VNet injection has **subnet-delegation and NSG requirements not settled in the repo**. |
| **How APIM reaches the cluster** | ADR-020 assumes **private** reach (VNet) to the internal ingress. Today `ingress-nginx` is **public** (see the Network and traffic path in [2-azure-services.md](../development/2-azure-services.md)). | → the bridge between "APIM assumes private backends" and "the current ingress is public" is **unresolved** — either front the existing public ingress hostname, or make the ingress internal-only first. Decide and record. |
| **DNS record** | Not settled by ADR-020: move the environment's hostname (`api.antkart.in` / the env subdomain) to APIM, or give APIM its own hostname. | **Moving the record makes the environment unreachable until APIM is ready** (30–45 min). For an ephemeral test, prefer giving APIM its own hostname and leaving the existing record intact. |
| **Certificate at the new edge** | ADR-020 says TLS terminates at APIM but does **not** say how the cert is supplied (APIM managed certificate, a Key Vault certificate, or the existing Let's Encrypt path). | → confirm; note the environment's in-cluster TLS is cert-manager + Let's Encrypt (5.7), which does **not** transfer to APIM automatically. |

> **Do not move the working environment's DNS record for this test.** Give APIM its own hostname. Moving the
> record makes the verified environment unreachable for the 30-45 minutes APIM takes to provision, and again
> for the same period on teardown — and if the phase is abandoned partway, the environment stays unreachable
> until the record is moved back manually. A separate hostname means the existing environment keeps working
> throughout and the test can be abandoned at any point with no cleanup beyond deleting APIM.

### 6A.3 Create

Provision the service. Pass the publisher identity as parameters — do not hardcode:

```powershell
$APIM        = "apim-antkart-<env>"        # follow the antkart-<resource> convention
$PUBLISHER   = "<publisher name>"
$PUBEMAIL    = "<publisher email>"
# $RG and $LOCATION are already set from earlier phases.

# ⚠️ Confirm --sku-name (ADR-020: Developer tier) and any VNet flags against current Azure docs
# before running — see the 6A.2 gaps. This is the create call; it returns before provisioning finishes.
az apim create --name $APIM --resource-group $RG --location $LOCATION `
  --publisher-name $PUBLISHER --publisher-email $PUBEMAIL `
  --sku-name Developer --no-wait
```

`--no-wait` returns immediately; **provisioning runs 30–45 minutes.** Poll rather than waiting blind:

```powershell
# Repeat until this reads "Succeeded"
az apim show --name $APIM --resource-group $RG --query "provisioningState" -o tsv
```

### 6A.4 Configure

Import the API and apply the policy chain. **Do not restate the chain here** — it is the
`validate-jwt → rate-limit/quota → subscription key/product → transform` sequence drawn in the
**APIM edge (target state)** section of [6-security.md](../development/6-security.md); author the APIM policy
XML to that diagram. (The repository does not commit APIM policy XML, so the exact policy document is written
at build time and corrected here afterward — → listed in the PR description.)

The `validate-jwt` policy MUST use **this** environment's Entra audience and tenant — the same values set in
the Helm layer in **section 5.3**:

- **Audience** — `api://antkart-api-<env>` (the per-environment App ID URI; the client-id GUID is also a
  valid audience form — see `ResolveValidAudiences` in the auth wiring).
- **Tenant / OpenID metadata** — tenant `4cacc56a-…` (single-tenant, same across environments), giving the
  Entra v2 OpenID configuration at
  `https://login.microsoftonline.com/4cacc56a-…/v2.0/.well-known/openid-configuration`.

> **The same trap that caught the Helm values applies here.** An audience left pointing at another
> environment (`api://antkart-api-dev` on a QA edge) produces a **401 that looks like a broken login** — the
> token is valid, but for a different audience. This is exactly the 5.3 audience trap, one layer further out.
> Grep the policy for the source environment's name before applying it, as in 5.3.

### 6A.5 Verify

An ordered sequence — each step proves one thing. Set `$APIM_HOST` to the APIM gateway hostname
(`az apim show --name $APIM --resource-group $RG --query "gatewayUrl" -o tsv`).

1. **APIM is reachable and rejects the anonymous request.** An unauthenticated call must return the
   rejection the `validate-jwt` policy defines (a 401), proving APIM is in the path and enforcing policy:
   ```powershell
   curl -i "$APIM_HOST/api/v1/products"     # expect 401 from the validate-jwt policy
   ```
2. **A valid token passes APIM and reaches the in-cluster gateway.** Call again with a bearer token, then
   prove the request actually arrived at `AK.Gateway` using the gateway's own logs (not just the APIM 200):
   ```powershell
   curl -i -H "Authorization: Bearer <token>" "$APIM_HOST/api/v1/products"   # expect 200
   kubectl logs -n antkart deploy/ak-gateway --since=2m | Select-String "products"
   ```
   A matching request line in the gateway log is the proof it traversed APIM → ingress → gateway.
3. **The full Postman collection passes through the new edge.** Point the collection's `baseUrl` at
   `$APIM_HOST` and run the `AntKart Cloud E2E Saga` collection (Collection Runner, 8000 ms delay). All 12
   steps must pass and the order must reach `Paid` — the full journey through the added edge.
4. **Telemetry still correlates.** A request through APIM must still produce a multi-role `OperationId` in
   the workspace — re-run the first Phase 6.6 query and confirm a single `OperationId` still spans
   `ak-gateway` + the backing services. APIM in front must not break the existing trace correlation.

### 6A.6 Destroy, and the name lock

> **APIM is soft-deleted, and the name is held.** Deleting the service does not free its name; the name
> remains reserved for the retention window unless the soft-deleted instance is purged. This is the same
> shape as **KI-007** (Key Vault purge protection, see [KNOWN_ISSUES.md](../KNOWN_ISSUES.md)) and will block
> recreating the same environment under the same name.
>
> Delete, then confirm the soft-deleted instance is listed, then purge it, then confirm it is gone. Do not
> consider this phase complete until the name is free.

```powershell
# 1 — delete the service (soft-delete; ~30–45 min)
az apim delete --name $APIM --resource-group $RG --yes

# 2 — confirm it is soft-deleted and its name is being held
#     ⚠️ confirm the exact 'deletedservice' command form against current Azure docs
az apim deletedservice list --query "[?name=='$APIM'].{name:name, location:location}" -o table

# 3 — purge the soft-deleted instance to release the name
az apim deletedservice purge --service-name $APIM --location $LOCATION

# 4 — confirm it is gone (must return nothing)
az apim deletedservice list --query "[?name=='$APIM'].name" -o tsv
```

Final cost check — confirm no APIM resource remains billing anywhere in the subscription:

```powershell
az apim list --query "[?contains(name, 'antkart')].{name:name, state:provisioningState}" -o table   # expect empty
```

> **This phase has never been executed.** Every command above is written from ADR-020 and 6-security.md,
> not from experience — the exact SKU flag, the VNet/subnet requirements, the policy XML, the certificate
> path, and the `deletedservice` command forms are all marked to confirm. The first person to run this
> should correct it against reality, the way Phases 0-6 were corrected, and remove the ⚠️ UNVERIFIED marker
> once it is proven.

---

## Phase 7 — Teardown and cost control

### Daily stop

```powershell
az aks stop --name $AKS --resource-group $RG
az postgres flexible-server stop -g $RG -n $PG

# Always verify — never assume
az aks show --name $AKS --resource-group $RG --query "powerState.code" -o tsv
az postgres flexible-server list --query "[].{name:name, state:state}" -o table
```

Both must read `Stopped`.

> **Azure force-starts a stopped flexible server after 7 days.** The stop command says so
> in its output. Set a reminder to re-stop it, or the environment resumes billing silently.

### Full teardown

Reverse wave order — dependents before dependencies:

```powershell
foreach ($u in @("role-assignments","workload-identity","function-app","github-oidc","key-vault","aks",
                 "governance","communication-services","eventgrid","servicebus","redis","postgresql",
                 "cosmosdb","container-registry","observability","networking",
                 "app-registration","resource-group")) {
  Write-Host "=== destroying $u ===" -ForegroundColor Yellow
  cd "$ENVDIR/$u"
  terragrunt destroy -auto-approve
}
cd $ENVDIR
```

Because purge protection is `false` (Decision C), the vault can be purged immediately so the
environment can be rebuilt under the same names:

```powershell
az keyvault purge --name $KV --location $LOCATION
az keyvault list-deleted --query "[?name=='$KV'].name" -o tsv    # must return nothing
```

Delete the state container only if you intend to abandon the environment permanently:

```powershell
az storage container delete --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login
```

---

## Appendix A — Resource inventory

| Unit | Azure resource | Name pattern | Wave |
|---|---|---|---|
| `resource-group` | Resource Group | `rg-antkart-<env>-eastus` | 0 |
| `app-registration` | Entra App Registration | `antkart-api-<env>` | 0 |
| `networking` | Virtual Network + 3 subnets | `vnet-antkart-<env>-eastus` | 1 |
| `observability` | Log Analytics + App Insights | `log-antkart-<env>`, `appi-antkart-<env>` | 1 |
| `container-registry` | Container Registry | `acrantkart<env>` | 1 |
| `cosmosdb` | Cosmos DB (Mongo API) | `cosmos-antkart-<env>` | 1 |
| `postgresql` | PostgreSQL Flexible Server | `psql-antkart-<env>-eus2` | 1 |
| `redis` | Azure Managed Redis | `redis-antkart-<env>` | 1 |
| `servicebus` | Service Bus Namespace | `sb-antkart-<env>` | 1 |
| `eventgrid` | Event Grid Topic | `evgt-antkart-<env>` | 1 |
| `communication-services` | Communication Services | `antkart-<env>` prefix | 1 |
| `governance` | Budget + policy | `budget-antkart-<env>` | 1 |
| `aks` | AKS Cluster | `aks-antkart-<env>` | 2 |
| `key-vault` | Key Vault | `kv-antkart-<env>` | 2 |
| `github-oidc` | Federated credentials for CI/CD | — | 2 |
| `function-app` | Function App + storage | `func-antkart-notifications-<env>` | 2 |
| `workload-identity` | 6 managed identities + federated creds | `ak-<service>` | 3 |
| `role-assignments` | RBAC grants | — | 3 |

## Appendix B — Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| Plan proposes destroying live dev resources | State container not changed | Stop. Check `root.hcl` line 31 |
| `BadArgumentError` on a telemetry query | Workspace schema sent to the classic API | Use `az monitor log-analytics query` |
| Key Vault create fails, name unavailable | Soft-deleted vault holds the name | `az keyvault list-deleted`; purge if protection was off |
| Federated credential token exchange fails | ServiceAccount name ≠ credential subject | Compare `system:serviceaccount:antkart:ak-<svc>` to the SA |
| Argo shows no change after a Git edit | Edited an Application/AppProject manifest | `kubectl apply` it — Argo does not watch itself |
| Argo will not re-sync after a failure | Argo does not auto-retry the same revision | Sync manually |
| `az` strips inner double quotes on Windows | PowerShell quoting | Single quotes inside a double-quoted KQL string |
| Postgres SKU unavailable in `eastus` | Regional offer restriction | Provision in `eastus2` |
| `InsufficientCapacity` on Redis apply | Region temporarily at capacity | Capacity is often transient — delete the failed resource, confirm `terragrunt state list` is empty, and retry before changing region. A successful create takes ~7 minutes; a capacity rejection fails in under 30 seconds. |
| Failed create left a resource holding the name | Azure records the shell even on failure | `az resource delete`, then verify `terragrunt state list` is empty before retrying |
| `unknown flag: --terragrunt-include-dir` | Flag renamed in newer Terragrunt | Plan per unit instead of run-all |
| `--name expected one argument` | Shell variables not set in this session | Re-run section 0.1 |
| `-o was unexpected at this time` | Windows `az` wrapper strips quotes; `(` breaks cmd parsing | Avoid parentheses in `--query`; use `--query "[].name"` then `.Count` in PowerShell |
| `ForbiddenByRbac` writing a Key Vault secret | No data-plane role on an RBAC-enabled vault | Grant Key Vault Secrets Officer at the vault scope; wait ~2 min |
| `check-acr` permission denied on a Temp path | Local filesystem, not Azure | Use `-f <path>`, or list role assignments on the ACR scope instead |
| `ForbiddenByRbac` listing secrets as yourself | The operator has no data-plane role on the new vault | Grant yourself Key Vault Secrets Officer; note this is separate from the service principal's grant |
| Pods crash-loop with a Key Vault 403 naming the WRONG vault | `KeyVault:Uri` defaults to the source environment in appsettings.json and is not overridden in Helm values | Add `KeyVault__Uri` to the new environment's values for every service |
| Secret stored truncated; shell reports `'x' is not recognized as an internal or external command` | Windows `az` wrapper splits the value at `&` | Use `--file` with a no-BOM UTF-8 file, not `--value` |
| `az resource invoke-action --action listKeys` returns `Not Found` for Managed Redis | Keys live on the `databases/default` sub-resource, not the cluster | Use the Terraform `connection_string` output, or `az rest` against `.../databases/default/listKeys` |
| ApplicationSet CRD fails with `metadata.annotations: Too long` | Client-side apply stores the manifest in an annotation exceeding 262144 bytes | Install Argo CD with `kubectl apply --server-side` |
| `conflict with "kubectl-client-side-apply"` on server-side apply | The two apply modes use different field managers | Add `--force-conflicts` when the values are identical |
| Pods `ImagePullBackOff` in a new environment | The new registry is empty, or `image.registry` still points at the source registry | Import the images (5.4) and set `image.registry` per environment (5.3) |
| Argo Application stuck `Progressing` with all pods Running | The gateway's Ingress has no address because no ingress controller is installed | Install ingress-nginx (5.7) |
| Argo `Synced`, revision matches HEAD, pods still on old config | ConfigMap changed; pod template did not, so no rollout | `kubectl rollout restart deploy/<svc>`; add a `checksum/config` annotation to the chart |
| `Bus start faulted` 401 `CreateOrUpdateSubscription`, pods still Healthy | MassTransit needs management-plane rights; Sender/Receiver are data-plane only | Grant `Azure Service Bus Data Owner` at namespace scope |
| `The Principal ID '<n>' is not valid`, `<n>` being a small number | Assigned to a reserved PowerShell variable (`$pid`); the read-only error scrolled past and the shell's value was used | Use a descriptive variable name such as `$principalId` |

## Appendix C — What this runbook does not yet cover

Honest gaps, to be closed as they are built:

- Seeding Cosmos with product data for the new environment.
- Razorpay test credentials and the payment verification path.
- The Notification Function's Event Grid subscription wiring — see section 5.9.
- An infrastructure CI/CD workflow. This runbook is its specification — automate it only after
  running it by hand at least once.

> **Two secret naming conventions coexist.** The source environment carries both `Section--Key` names
> (which .NET configuration binds automatically) and kebab-case names such as `cosmos-connection-string`
> and `servicebus-connection-string`. Some appear to duplicate each other — `cosmos-connection-string`
> and `MongoDbSettings--ConnectionString` may hold the same value. Before seeding a new environment,
> confirm which names the application actually reads; recreating unused secrets copies the confusion
> forward.

## Appendix D — Resource inventory and permissions

A complete environment is **18 Terragrunt units producing 86 Azure resources**. The per-unit counts
below were observed on a real build; use them to sanity-check a `plan` before approving it. A count
that differs means the configuration has drifted. Resource types are read from
`infrastructure/modules/<unit>/*.tf`; where a count comes from a `count`/`for_each` that is noted.

| Unit | Wave | Resources created | Permission plane required |
|---|---|---|---|
| `resource-group` | 0 | **1** — `azurerm_resource_group` | Azure RBAC |
| `app-registration` | 0 | **5** — `azuread_application` ×2 (the API + a test client), `azuread_service_principal` ×2, `azuread_application_pre_authorized` | Entra ID directory (Cloud Application Administrator) — only azuread resources, no Azure resource |
| `networking` | 1 | **10** — `azurerm_virtual_network` (1); `azurerm_subnet`, `azurerm_network_security_group`, `azurerm_subnet_network_security_group_association` (3 each, `for_each` over the 3 subnets) | Azure RBAC |
| `observability` | 1 | **2** — `azurerm_log_analytics_workspace`, `azurerm_application_insights` | Azure RBAC |
| `container-registry` | 1 | **1** — `azurerm_container_registry` | Azure RBAC |
| `cosmosdb` | 1 | **2** — `azurerm_cosmosdb_account`, `azurerm_cosmosdb_mongo_database` | Azure RBAC |
| `postgresql` | 1 | **8** — `random_password` (1), `azurerm_postgresql_flexible_server` (1), `azurerm_postgresql_flexible_server_database` (4, `for_each` over the 4 database names), `azurerm_postgresql_flexible_server_firewall_rule` (2) | Azure RBAC |
| `redis` | 1 | **1** — `azurerm_managed_redis` | Azure RBAC |
| `servicebus` | 1 | **7** — `azurerm_servicebus_namespace` (1), `azurerm_servicebus_queue` (1, `for_each` — `order-commands`), `azurerm_servicebus_topic` (1), `azurerm_servicebus_subscription` (4, `for_each` over the consuming services) | Azure RBAC |
| `eventgrid` | 1 | **1** — `azurerm_eventgrid_topic` | Azure RBAC |
| `communication-services` | 1 | **4** — `azurerm_email_communication_service`, `azurerm_email_communication_service_domain`, `azurerm_communication_service`, `azurerm_communication_service_email_domain_association` | Azure RBAC |
| `governance` | 1 | **1** — `azurerm_consumption_budget_resource_group` | Azure RBAC |
| `aks` | 2 | **2** — `azurerm_kubernetes_cluster`, `azurerm_role_assignment` (kubelet AcrPull) | Azure RBAC (incl. RBAC Administrator for the role assignment) |
| `key-vault` | 2 | **2** — `azurerm_key_vault`, `azurerm_key_vault_secret` (`ApplicationInsights--ConnectionString`, `count = 1` when supplied) | Azure RBAC **+ Key Vault data plane** (Secrets Officer — writes a secret) |
| `github-oidc` | 2 | **4** — `azurerm_user_assigned_identity` (1), `azurerm_federated_identity_credential` (2, `for_each` over the master + environment subjects), `azurerm_role_assignment` (AcrPush) | Azure RBAC (incl. RBAC Administrator) — creates a managed identity + federated cred, not an app registration |
| `function-app` | 2 | **3** — `azurerm_service_plan`, `azurerm_storage_account`, `azurerm_linux_function_app` | Azure RBAC |
| `workload-identity` | 3 | **28** — `azurerm_user_assigned_identity` (6, `for_each` over the services), `azurerm_federated_identity_credential` (6), `azurerm_role_assignment` (16, `for_each` over the per-service role map) | Azure RBAC (incl. RBAC Administrator) |
| `role-assignments` | 3 | **4** — `azurerm_role_assignment` ×4 (the Function App identity's KV Secrets User, SB Data Receiver, SB Data Sender, EventGrid Data Sender) | Azure RBAC (RBAC Administrator — only role assignments) |

Total: **86** resources across the 18 units.

Three of these planes are separate systems and a role in one grants nothing in the others — see
section 1.1.1. In this inventory only `app-registration` touches the Entra directory, and only
`key-vault` writes to the Key Vault data plane; every other unit needs Azure RBAC alone.

### Data that must be populated after provisioning

Terraform creates empty resources; several need content before the platform runs.

| Resource | What must be populated | Source | Covered in |
|---|---|---|---|
| Key Vault | **9 secrets** — 1 written by Terraform (`ApplicationInsights--ConnectionString`) + 8 seeded manually: `ConnectionStrings--Postgres`, `ConnectionStrings--PaymentsDb`, `ConnectionStrings--DiscountDb`, `ConnectionStrings--Notifications`, `MongoDbSettings--ConnectionString`, `RedisSettings--ConnectionString`, `Razorpay--KeyId`, `Razorpay--KeySecret`. Do **NOT** create `cosmos-connection-string` or `servicebus-connection-string` — no consumer ([KI-011](../KNOWN_ISSUES.md)) | Terraform (App Insights) + operator (the 8) | Phase 4 |
| Cosmos DB | The product catalogue — empty on creation; **300 documents** seeded by the products service at startup, only when `Seeding__RunOnStartup` is `"true"` | AK.Products startup seeder | 6.4 |
| PostgreSQL | Schema for the four databases. Order, Payments and Discount apply EF Core migrations at startup (`MigrateAsync` in `Program.cs`); the Notification Function ships an `InitialCreate` migration but **no startup migrate call was found** — see section 5.9 | EF Core migrations at service startup | 6.4 |
| Service Bus | The topic and subscriptions exist from Terraform, but MassTransit reconciles subscription properties and rules at startup and needs **`Azure Service Bus Data Owner`** to do it | MassTransit at startup + the Data Owner grant | Phase 5 callout (after 5.5) |
| Container registry | Images — empty on creation | `az acr import` from the source registry | 5.4 |
| DNS | The A record for the environment's hostname → the ingress public IP | Registrar / Azure DNS | 5.7 |

An environment whose 86 resources all exist is still not a working platform until this table is complete.

---

**Related:** [Infrastructure Guide](infrastructure-guide.md) · [GitOps Guide](gitops-guide.md) ·
[Operations Command Reference](operations-command-reference.md) ·
[Known Issues](../KNOWN_ISSUES.md)
