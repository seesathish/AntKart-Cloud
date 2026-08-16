using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Resilience;
using MassTransit;
using AK.Products.Application.Consumers;
using AK.Products.Application.Interfaces;
using AK.Products.Infrastructure.Grpc;
using AK.Products.Infrastructure.Persistence;
using AK.Products.Infrastructure.Persistence.Repositories;
using AK.Products.Infrastructure.Resilience;
using AK.Products.Infrastructure.Seeders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Products.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ProductClassMap.Register();

        // Non-secret values (database + collection names) come from appsettings.
        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDbSettings"));

        // M3 Step 4: the Cosmos DB (MongoDB API) connection string is a SECRET — it is never
        // committed. It is loaded into configuration from Key Vault (by the Step 1 foundation)
        // under the secret name "ProductsCosmosConnectionString". When present it sets the
        // connection string; otherwise the non-secret local default (mongodb://localhost:27017)
        // applies, so offline development still works.
        services.PostConfigure<MongoDbSettings>(s =>
        {
            var cosmosConnectionString = configuration["ProductsCosmosConnectionString"];
            if (!string.IsNullOrWhiteSpace(cosmosConnectionString))
                s.ConnectionString = cosmosConnectionString;
        });

        // Cosmos DB throttles under load (429) and tells the client how long to wait. Register a
        // retry pipeline that honours that Retry-After (falling back to exponential backoff + jitter
        // otherwise). The ProductRepository runs every data-store call through this pipeline — the
        // retry lives at the call site, where idempotency and the CancellationToken are known. The
        // Cosmos-specific transient/Retry-After rules come from CosmosResilience; the mechanism is
        // the shared BuildingBlocks AddDataStoreRetry.
        services.AddDataStoreResiliencePipeline(
            "cosmos", CosmosResilience.IsTransient, CosmosResilience.GetRetryAfter);

        services.AddSingleton<MongoDbContext>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ProductSeeder>();

        // AK.Discount is an OPTIONAL price-enrichment dependency: the catalogue must render whether
        // or not discount data is available. So this client is tuned to FAIL FAST and degrade
        // quietly — the deliberate opposite of the PATIENT resilience used for CRITICAL stores
        // (Cosmos's Retry-After pipeline above, Service Bus's message retry). A down Discount
        // service must never slow a product query.
        //   * 2s HttpClient timeout — a hard per-call ceiling (was 10s).
        //   * AddOptionalDependencyResilience — NO retry (retrying a down optional service only
        //     multiplies latency) + a circuit breaker that opens after a couple of failures and
        //     stays open for a cooldown, so once Discount is known-down, subsequent product queries
        //     skip the call entirely and return immediately with no discount.
        services.Configure<DiscountGrpcSettings>(configuration.GetSection("DiscountGrpc"));
        services.AddHttpClient("discount-grpc", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(2);
        })
        .AddOptionalDependencyResilience();
        services.AddScoped<IDiscountGrpcClient, DiscountGrpcClient>();

        services.AddAzureServiceBusMassTransit(
            configuration,
            x => x.AddConsumer<ReserveStockConsumer>(),
            // Bind the provisioned "products" subscription on the integration-events topic.
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("products", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureConsumer<ReserveStockConsumer>(context);
                });
            });

        return services;
    }
}
