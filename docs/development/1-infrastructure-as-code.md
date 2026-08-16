# Infrastructure as code — how the cloud gets built

> **Diagrams pending review:** _Terragrunt unit dependencies_ and _Environment promotion_ are carried across as-is and will be reworked.

Every Azure resource is provisioned as code. **Terraform modules** describe *how* a resource is built; **Terragrunt live units** wire the modules together for an environment and supply their inputs. A shared `root.hcl` generates the `backend`, `provider`, and `versions` configuration into each unit, so that configuration lives in exactly one place. Remote state is isolated **per unit** in Azure Storage, with blob-lease locking serialising applies. The `dev` environment is delivered; `qa` is planned.

## Terragrunt unit dependencies

```mermaid
flowchart TB
    RG["resource-group"]:::paas
    APPREG["app-registration<br/>(independent · directory-plane)"]:::identity
    AKS["aks"]:::paas
    FN["function-app"]:::paas
    OIDC["github-oidc"]:::identity
    WI["workload-identity"]:::identity
    RA["role-assignments"]:::identity

    subgraph RGONLY["11 units — depend only on resource-group"]
        NET["networking"]:::paas
        ACR["container-registry"]:::paas
        KV["key-vault"]:::identity
        OBS["observability"]:::paas
        COS["cosmosdb"]:::datastore
        PG["postgresql"]:::datastore
        RED["redis"]:::datastore
        SB["servicebus"]:::paas
        EVG["eventgrid"]:::paas
        ACS["communication-services"]:::paas
        GOV["governance"]:::paas
    end

    RGONLY --> RG
    AKS --> RG
    AKS --> NET
    AKS --> ACR
    AKS --> OBS
    FN --> RG
    FN --> OBS
    OIDC --> RG
    OIDC --> ACR
    WI --> RG
    WI --> AKS
    WI --> KV
    WI --> SB
    WI --> EVG
    RA --> FN
    RA --> KV
    RA --> SB
    RA --> EVG

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **18 live units.** `environments/dev` holds **18** unit folders; the state-backend bootstrap is an `az` step, not a Terragrunt unit.
- **`resource-group` is the root** — everything except `app-registration` traces back to it; **11 units depend on it alone** and nothing else.
- **`app-registration` is independent** — it manages a directory object via the `azuread` provider, not a resource-group resource.
- **Two composite tails carry the interesting order:** `role-assignments` waits on `function-app` + `key-vault` + `servicebus` + `eventgrid`; `workload-identity` waits on `aks` (for its OIDC issuer) + `key-vault` + `servicebus` + `eventgrid`.
- A *complete* dependency graph must show every unit; the 11 resource-group-only units are grouped to keep it legible.

## Environment promotion — dev vs QA

```mermaid
flowchart TB
    MODULES["infrastructure/modules<br/>(shared, environment-agnostic)"]:::cicd

    subgraph DEV["environments/dev — delivered"]
        DUNITS["18 Terragrunt units (dev inputs)"]:::cicd
        DSTATE[("state key = path_relative_to_include<br/>e.g. aks/terraform.tfstate")]:::datastore
    end

    subgraph QA["environments/qa — PLANNED"]
        QUNITS["same modules, qa inputs"]:::cicd
        QSTATE[("state key = path_relative_to_include<br/>e.g. aks/terraform.tfstate")]:::datastore
    end

    RISK["State-key collision risk:<br/>the key derives from the unit path only, NOT the environment.<br/>QA must use a distinct backend container or key prefix,<br/>or its state overwrites dev."]:::issue
    PLAN["QA planned — not yet created"]:::issue

    MODULES --> DUNITS
    MODULES --> QUNITS
    DUNITS --> DSTATE
    QUNITS --> QSTATE
    RISK -.-> QSTATE
    PLAN -.-> QA

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **Modules are shared, environments differ only by inputs:** promotion means a second `environments/qa` tree that reuses `infrastructure/modules` with QA-specific inputs — not copied module code.
- **The state key is the risk (red):** `root.hcl` derives the backend key from `path_relative_to_include()` — the **unit path only**, with no environment segment. A QA tree using the same backend container would produce identical keys (`aks/terraform.tfstate`, …) and **overwrite dev state**.
- **The fix QA must adopt:** a distinct backend `container_name` or a per-environment key prefix, so dev and QA state can never collide.
- **QA is planned, not built** — both the QA subgraph and the risk are drawn as red-dashed to keep that explicit.

## How it was built

- Concepts first: [IaC fundamentals](../guides/iac-concepts.md).
- Step-by-step provisioning (per resource, Understand → Build → Execute → Verify): [Infrastructure Guide](../guides/infrastructure-guide.md) · the IaC map in [infrastructure/README](../../infrastructure/README.md).

## Decisions

- [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](../adr/ADR-012-iac-with-terraform-terragrunt.md)

## Open items

- The **QA environment is planned**, not built; the state-key collision risk above must be resolved before a second environment shares the backend. See the [Roadmap](../ROADMAP.md).
