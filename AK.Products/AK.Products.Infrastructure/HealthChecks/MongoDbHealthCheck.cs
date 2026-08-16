using AK.Products.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AK.Products.Infrastructure.HealthChecks;

// DEEP dependency check: is Cosmos DB (MongoDB API) actually reachable and answering?
//
// It performs a real round-trip ({ ping: 1 }) and therefore MUST NEVER be wired to the liveness
// probe — a Cosmos blip would otherwise restart every pod at once (a restart storm). It is
// registered with the Deep tag, so it surfaces only on the diagnostic endpoint (/health/deps).
public sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly MongoDbContext _context;

    public MongoDbHealthCheck(MongoDbContext context) => _context = context;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy("Cosmos DB (MongoDB API) reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB (MongoDB API) unreachable.", ex);
        }
    }
}
