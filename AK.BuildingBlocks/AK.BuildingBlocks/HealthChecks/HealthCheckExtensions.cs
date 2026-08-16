using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AK.BuildingBlocks.HealthChecks;

// THREE health surfaces, three jobs — wired identically into every service so probes are
// consistent platform-wide. The Kubernetes probes that CONSUME these are connected in the AKS
// milestone; this step EXPOSES the endpoints and gets the safe mapping right.
//
//   /health/live   LIVENESS — "is the process alive?" SHALLOW: Healthy if the app is running; it
//                  makes NO external calls. WHY shallow: a failed liveness probe RESTARTS the pod.
//                  If liveness checked Cosmos / Service Bus, one dependency blip would restart
//                  every pod at once — a restart storm that turns a brief blip into an outage.
//
//   /health/ready  READINESS — "should this pod receive traffic?" May include LIGHTWEIGHT checks
//                  but is TOLERANT. WHY tolerant: a failed readiness probe pulls the pod OUT of the
//                  load balancer. If readiness failed on a SHARED-dependency blip, every pod would
//                  drop out together — a fleet-wide blackout. So shared-dependency checks placed
//                  here should report Degraded (not Unhealthy); Degraded still serves traffic (200).
//
//   /health/deps   DIAGNOSTICS — deep dependency checks (Cosmos, Service Bus, Key Vault). For
//                  humans/dashboards, NOT a probe. It MAY return 503 when a dependency is down;
//                  nothing restarts or de-registers a pod because of it.
public static class HealthCheckExtensions
{
    // Registers the single always-on check: a shallow "self" probe tagged for BOTH liveness and
    // readiness (process up ⇒ live and baseline-ready). Returns the builder so a service can chain
    // its own DEEP checks (e.g. .AddCheck<CosmosCheck>(..., tags: [Deep])) — those land only on the
    // diagnostic endpoint and never touch liveness/readiness.
    public static IHealthChecksBuilder AddDefaultHealthChecks(this IServiceCollection services)
    {
        return services.AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("Process is responsive."),
                tags: new[] { HealthCheckTags.Live, HealthCheckTags.Ready });
    }

    public static WebApplication MapDefaultHealthChecks(this WebApplication app)
    {
        // LIVENESS — only Live-tagged checks (self). Because checks excluded by the predicate are
        // NOT executed, no external dependency is ever touched on this endpoint.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Live),
            AllowCachingResponses = false
        });

        // READINESS — only Ready-tagged checks. TOLERANT: Degraded is mapped to 200 (still serving),
        // so a shared-dependency wobble registered as Degraded-on-failure removes no pod from
        // rotation. Only an explicit Unhealthy (the app genuinely cannot serve) returns 503.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            AllowCachingResponses = false
        });

        // DIAGNOSTICS — ALL checks (self + every deep dependency, plus any third-party bus check).
        // Detailed JSON for humans. NOT a probe target, so a 503 here is informational only.
        app.MapHealthChecks("/health/deps", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedJson,
            AllowCachingResponses = false
        });

        // Backward-compatible alias, kept SHALLOW (self only) so existing monitors that hit
        // /health are unaffected by the new deep checks.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Live),
            AllowCachingResponses = false
        });

        return app;
    }

    private static Task WriteDetailedJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds,
                tags = e.Value.Tags,
                error = e.Value.Exception?.Message
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
