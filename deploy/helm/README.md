# AntKart Helm Charts

Helm packaging for the six deployable AntKart services on AKS (`aks-antkart-dev`).
`AK.Notification.Functions` is an Azure Function and is **not** part of this — it is
deployed through the serverless path, not the cluster.

## Structure — one generic chart, six values files

```
deploy/helm/
├── antkart-service/          # a single GENERIC chart, instantiated once per service
│   ├── Chart.yaml
│   ├── values.yaml           # shared defaults (probes, resources, common env, security)
│   └── templates/
│       ├── _helpers.tpl
│       ├── serviceaccount.yaml   # workload-identity ServiceAccount (client-id from values)
│       ├── configmap-env.yaml    # non-secret config -> envFrom
│       ├── configmap-ocelot.yaml # gateway only: ocelot.json from values (mounted as a file)
│       ├── deployment.yaml       # pod-template use label + serviceAccountName; probes
│       ├── service.yaml          # ClusterIP on 8080
│       └── NOTES.txt
├── values/                   # per-service values (the only thing that differs)
│   ├── products.yaml   cart.yaml   order.yaml
│   ├── payments.yaml   discount.yaml   gateway.yaml
└── README.md
```

**Why a single generic chart, not an umbrella or a library sub-chart.** The six
services are structurally identical — each is a ServiceAccount + Deployment +
ClusterIP Service + a ConfigMap, wired the same way, differing only in name,
image, workload-identity client-id, probe type, and a handful of config values.
A single parameterised chart instantiated six times expresses that with zero
template duplication and lets each service be installed, upgraded, and rolled back
independently. An **umbrella** chart would add a parent release that couples the
six lifecycles together for no benefit (we deploy services independently, not as
one unit); a **library** chart would add indirection without removing any more
duplication than the generic chart already does. If the services later diverge
structurally, individual charts can depend on a shared library chart at that point.

## Install / upgrade a service

Each service is its own Helm release, named after the service, in the `antkart`
namespace. Use `upgrade --install` so the same command installs or updates:

```bash
cd deploy/helm

# one service
helm upgrade --install ak-products antkart-service \
  -n antkart --create-namespace \
  -f values/products.yaml

# all six
for s in products cart order payments discount gateway; do
  helm upgrade --install ak-$s antkart-service -n antkart -f values/$s.yaml
done
```

Override the image tag at deploy time (e.g. an immutable commit SHA):

```bash
helm upgrade --install ak-products antkart-service -n antkart \
  -f values/products.yaml --set image.tag=<git-sha>
```

Validate without deploying:

```bash
helm lint antkart-service -f values/products.yaml
helm template ak-products antkart-service -n antkart -f values/products.yaml
```

## How values map to the workload-identity client-ids

Each service runs under its own user-assigned managed identity (`id-ak-<service>-dev`,
provisioned by the `workload-identity` Terraform unit). The chart writes that
identity's **client-id** onto the ServiceAccount as
`azure.workload.identity/client-id`, and labels the **pod template**
`azure.workload.identity/use: "true"`. Both are required for the webhook to inject
`AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_FEDERATED_TOKEN_FILE` /
`AZURE_AUTHORITY_HOST`, after which `DefaultAzureCredential` reads Key Vault (and
Service Bus / Event Grid) with **no stored secret**.

| Service | Release / SA name | `workloadIdentityClientId` (in values) |
|---------|-------------------|----------------------------------------|
| Products | `ak-products` | `c2f1d5e5-fe15-48bd-b75f-d7e820e9c687` |
| ShoppingCart | `ak-cart` | `53d89789-5bfa-4bd8-b0ee-9ca382834a22` |
| Order | `ak-order` | `4073b3dc-ca1e-4788-a58d-c2b4393ee7b9` |
| Payments | `ak-payments` | `31c739a1-51f3-4e4d-b322-5df23dc66177` |
| Discount | `ak-discount` | `9c3ddaad-0cd2-4b98-9bbc-d9cd9ddaa4e2` |
| Gateway | `ak-gateway` | `a2b00f18-e8ff-4ab0-a399-5b194a2f05e1` |

The subject the federated credential trusts is
`system:serviceaccount:antkart:ak-<service>` — the namespace and SA name the chart
produces must match it exactly (case-sensitive).

## Configuration

Non-secret config is rendered into a per-service `ConfigMap` and injected via
`envFrom`. .NET maps `__` to the config `:` separator (`KeyVault__Uri` →
`KeyVault:Uri`). **Secrets are never here** — connection strings, Razorpay keys,
etc. are read from Key Vault at runtime via workload identity. Values sourced from
[docs/guides/container-configuration.md](../../docs/guides/container-configuration.md).

In-cluster DNS replaces the localhost defaults baked into the images:

| Service | Key | Value |
|---------|-----|-------|
| Order | `ProductsApi__BaseUrl` | `http://ak-products:8080/` |
| Products | `DiscountGrpc__Address` | `http://ak-discount:8080` |
| Gateway | Ocelot `DownstreamHostAndPorts` | `ak-products` / `ak-cart` / `ak-order` / `ak-payments` on `8080` |

The gateway's `ocelot.json` is mounted from a ConfigMap over `/app/ocelot.json`, so
routing changes need only a values edit + `helm upgrade` — no image rebuild.

## Probes

A `startupProbe` gives the process up to ~150s (`failureThreshold 30 × periodSeconds 5`)
to load Key Vault and bind Kestrel before the liveness/readiness probes begin, so a
slow boot never triggers a restart. Liveness hits shallow `/health/live` (no external
calls); readiness hits tolerant `/health/ready`.

## AK.Discount — gRPC over HTTP/2 (h2c)

Discount serves **HTTP/2 (h2c) only**, so an HTTP/1.1 `httpGet` probe would be
rejected — its chart uses **TCP** probes (`probes.type: tcp`) instead. Its Service
port is named `grpc` with `appProtocol: grpc` (metadata only for a ClusterIP; the
in-cluster caller reaches it as `http://ak-discount:8080` over h2c).

**When ingress is added (next step), the discount path needs HTTP/2 end-to-end** —
the ingress/mesh backend protocol must be h2c/gRPC (e.g. a Gateway API `GRPCRoute`,
or NGINX `nginx.ingress.kubernetes.io/backend-protocol: GRPC`). A plain HTTP/1.1
ingress in front of Discount will break gRPC. Also note Discount is an **internal**
dependency (called by Products); it does not need to be exposed externally.

## Not included yet

No Ingress, TLS, HPA, or NetworkPolicy — these are the next step. Services are
`ClusterIP` (reachable only inside the cluster).
