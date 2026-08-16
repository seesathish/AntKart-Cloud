# C4 diagram renders

This folder holds the **exported SVG images** of the eight hero diagrams. The repository root [`README.md`](../../../README.md) embeds them (light + dark). Each hero diagram is authored in a Structurizr workspace that is maintained separately from this repository; only the exported SVGs are published here.

## The eight hero diagrams

| Folder | View key | Exported files | Status |
|--------|----------|----------------|--------|
| `hero-system/` | `SystemOverview` | `SystemOverview.svg` · `SystemOverview-dark.svg` | Drawn |
| _hero-platform (folder removed — source in git history)_ | `PlatformArchitecture` | `PlatformArchitecture.svg` · `PlatformArchitecture-dark.svg` | Drawn |
| `hero-infrastructure/` | `InfrastructureAsCode` | `InfrastructureAsCode.svg` · `InfrastructureAsCode-dark.svg` | Drawn |
| `hero-azure/` | `AzureServices` | `AzureServices.svg` · `AzureServices-dark.svg` | Drawn |
| `hero-kubernetes/` | `Kubernetes` | `Kubernetes.svg` · `Kubernetes-dark.svg` | Drawn |
| `hero-devops/` | `DevOps` | `DevOps.svg` · `DevOps-dark.svg` | Drawn |
| `hero-observability/` | `Observability` | `Observability.svg` · `Observability-dark.svg` | Drawn |
| `hero-security/` | `Security` | `Security.svg` · `Security-dark.svg` | Drawn |

All eight hero diagrams are now hand-arranged and exported — the root [`README.md`](../../../README.md) embeds the full set (light + dark).

## Authoring

- **The rendered SVGs live here.** The eight diagrams above — light and dark — are committed in this `renders/` folder, and the root [`README.md`](../../../README.md) embeds them directly.
- **The Structurizr sources are maintained separately.** The `.dsl`/`.json` workspaces that produce these SVGs are kept outside this repository; only the exported images are published here.
- **Mermaid diagrams are authored inline.** Every other diagram in the documentation is a Mermaid code block written directly inside the markdown file that uses it, so it renders on GitHub with no separate tooling.
