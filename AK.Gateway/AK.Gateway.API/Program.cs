using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.Configuration;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Observability;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Middleware;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

// AK.Gateway is the single entry point for all external traffic.
// Clients never call downstream services (Products, Cart, Order, etc.) directly —
// everything goes through this gateway, which listens on port 8080 in the cluster.
//
// Ocelot routes are defined in ocelot.json (in-cluster ak-* Service names) or
// ocelot.Development.json (localhost ports for local runs) — see the config loading below.
// Each route specifies: upstream path, downstream service, rate limit, and QoS circuit breaker.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// WebApplication.CreateBuilder already loads appsettings.json, appsettings.{Environment}.json,
// environment variables and the command line — so only the Ocelot ROUTING config is added here.
//
// Load EXACTLY ONE ocelot file, selected by environment. The two files are FULL REPLACEMENTS of
// one another — they define the same upstream routes with different downstreams:
//   * ocelot.json             — in-cluster ak-* Service names on 8080; the DEFAULT, used in
//                               Production / AKS (and what the Helm ocelot ConfigMap supplies at
//                               /app/ocelot.json).
//   * ocelot.Development.json — localhost downstream ports, for running the gateway locally
//                               against services started on the host. Used only in Development.
//
// Loading BOTH (the previous `AddJsonFile("ocelot.json")` + `AddJsonFile("ocelot.{env}.json")`)
// merged their Routes arrays BY INDEX, which left the same upstream template defined twice and
// made Ocelot refuse to start ("route ... has duplicate"). Ocelot's own AddOcelot(...) merger is
// no help here either: it CONCATENATES routes across files (so a full-replacement override still
// duplicates), and its on-disk merge cannot write to the read-only ConfigMap mount in the cluster.
// Selecting a single file avoids the merge entirely — both profiles start and route correctly.
var ocelotFile = $"ocelot.{builder.Environment.EnvironmentName}.json";
if (!File.Exists(Path.Combine(builder.Environment.ContentRootPath, ocelotFile)))
{
    ocelotFile = "ocelot.json";
}
builder.Configuration.AddJsonFile(ocelotFile, optional: false, reloadOnChange: true);

// The gateway reads no secrets of its own (Ocelot routes come from a ConfigMap and the Entra
// JWT validation settings are non-secret), but it still needs Key Vault as a configuration
// source so ApplicationInsights:ConnectionString resolves — otherwise OpenTelemetry collects the
// gateway's traces but cannot export them to Application Insights. Must run BEFORE
// AddOpenTelemetryObservability below, or the connection string is not available to the exporter.
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

builder.AddSerilogLogging();
builder.AddOpenTelemetryObservability("AK.Gateway.API");

// JWT validation at the gateway edge — Ocelot checks the Bearer token before forwarding the request.
// Downstream services also validate JWTs independently (defence in depth).
builder.Services.AddEntraAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();

// Required by Ocelot's rate limiting middleware — stores per-route request counters.
// Without this, EnableRateLimiting in ocelot.json is silently ignored.
builder.Services.AddMemoryCache();

// AddPolly enables Ocelot's QoS (circuit breaker) per route, configured in ocelot.json.
builder.Services.AddOcelot(builder.Configuration).AddPolly();

// AllowAll CORS: permits frontend apps from any origin to call the gateway.
// In production, replace AllowAnyOrigin with a specific allowed origins list.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// Attach X-Correlation-Id to every request so logs from all downstream services
// can be correlated in Azure Monitor (Log Analytics / Application Insights) by the same ID.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseEntraAuth();

// The gateway's OWN health endpoints — /health, /health/live, /health/ready, /health/deps —
// wired through the same shared AK.BuildingBlocks mechanism every other service uses, so the
// gateway is consistent with the platform. The registered check is the shallow "self" check
// (no downstream calls), so liveness/readiness never depend on a downstream service.
app.MapDefaultHealthChecks();

// CRITICAL — Ocelot's middleware is TERMINAL: any path that reaches it is treated as a proxy
// request, and anything without a matching downstream route gets a 404. Mapping the health
// endpoints "before" UseOcelot is NOT sufficient, because WebApplication defers endpoint
// EXECUTION to the end of the pipeline (UseEndpoints) — Ocelot short-circuits first, so
// /health/* would still 404 and the Kubernetes probe would restart-loop the pod.
//
// Fix: run Ocelot ONLY for non-/health paths via MapWhen, so /health/* bypasses Ocelot
// entirely and falls through to endpoint routing, which executes the mapped health checks.
// Every proxied upstream route is under /gateway/*, so excluding /health removes nothing
// Ocelot needs to serve. UseOcelot is async; it configures the branch pipeline once at
// startup, so resolving it synchronously here is safe (it is not a per-request call).
app.MapWhen(
    context => !context.Request.Path.StartsWithSegments("/health"),
    ocelotApp => ocelotApp.UseOcelot().GetAwaiter().GetResult());

app.Run();

public partial class Program { }
