# Infrastructure as code — how the cloud gets built

> **Diagrams pending review:** _Terragrunt unit dependencies_ and _Environment promotion_ are carried across as-is and will be reworked.

Every Azure resource is provisioned as code. **Terraform modules** describe *how* a resource is built; **Terragrunt live units** wire the modules together for an environment and supply their inputs. A shared `root.hcl` generates the `backend`, `provider`, and `versions` configuration into each unit, so that configuration lives in exactly one place. Remote state is isolated **per unit** in Azure Storage, with blob-lease locking serialising applies. Both the `dev` and `qa` environments are delivered, built from the same modules with different inputs.

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
        DSTATE[("container: tfstate<br/>key = aks/terraform.tfstate")]:::datastore
    end

    subgraph QA["environments/qa — delivered"]
        QUNITS["same modules, qa inputs"]:::cicd
        QSTATE[("container: tfstate-qa<br/>key = aks/terraform.tfstate")]:::datastore
    end

    SOLVED["Isolation by CONTAINER, not key:<br/>the key derives from the unit path only, so dev/aks and qa/aks<br/>resolve to the identical key — each environment writes to its own<br/>storage container, so the state blobs never collide."]:::service

    MODULES --> DUNITS
    MODULES --> QUNITS
    DUNITS --> DSTATE
    QUNITS --> QSTATE
    SOLVED -.-> DSTATE
    SOLVED -.-> QSTATE

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

- **Modules are shared, environments differ only by inputs:** `environments/dev` and `environments/qa` are both built — each a tree of the same 18 units reusing `infrastructure/modules` with its own inputs, not copied module code.
- **The state key is identical by design:** `root.hcl` derives the backend key from `path_relative_to_include()` — the **unit path only**, with no environment segment — so `dev/aks` and `qa/aks` both resolve to `aks/terraform.tfstate`.
- **Isolation is by container, not by key:** each environment writes to its **own storage container** (dev → `tfstate`, qa → `tfstate-qa`), so the identical keys never collide. Changing the key expression to add an environment segment would **orphan the existing state blobs** — the container is the seam, and it is already in place.

## How it was built

- Concepts first: [IaC fundamentals](../guides/iac-concepts.md).
- Step-by-step provisioning (per resource, Understand → Build → Execute → Verify): [Infrastructure Guide](../guides/infrastructure-guide.md) · the IaC map in [infrastructure/README](../../infrastructure/README.md).

## Decisions

- [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](../adr/ADR-012-iac-with-terraform-terragrunt.md)

## Open items

- **Resolved:** `qa` is built alongside `dev`, and the state-key collision is handled by each environment using its own backend storage container (`tfstate` for dev, `tfstate-qa` for qa), so their identical keys never collide. See the [Roadmap](../ROADMAP.md).
