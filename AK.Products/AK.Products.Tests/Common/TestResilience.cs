using AK.BuildingBlocks.Resilience;
using AK.Products.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;

namespace AK.Products.Tests.Common;

// Builds a real "cosmos" resilience pipeline provider for tests, using a tiny base delay so retry
// tests run fast. Mirrors the production registration (AddDataStoreRetry with CosmosResilience).
public static class TestResilience
{
    public static ResiliencePipelineProvider<string> CosmosPipelines(
        int maxRetryAttempts = 3)
    {
        var services = new ServiceCollection();
        services.AddDataStoreResiliencePipeline(
            "cosmos",
            CosmosResilience.IsTransient,
            CosmosResilience.GetRetryAfter,
            maxRetryAttempts: maxRetryAttempts,
            baseDelay: TimeSpan.FromMilliseconds(1));

        return services.BuildServiceProvider().GetRequiredService<ResiliencePipelineProvider<string>>();
    }
}
