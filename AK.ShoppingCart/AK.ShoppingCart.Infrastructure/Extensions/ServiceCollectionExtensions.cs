using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Resilience;
using MassTransit;
using AK.ShoppingCart.Application.Consumers;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Infrastructure.Persistence;
using AK.ShoppingCart.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.ShoppingCart.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisSettings>(configuration.GetSection("RedisSettings"));
        services.AddSingleton<RedisContext>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddRedisResilience();

        services.AddAzureServiceBusMassTransit(
            configuration,
            x => x.AddConsumer<ClearCartOnOrderConfirmedConsumer>(),
            // Bind the provisioned "cart" subscription on the integration-events topic.
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("cart", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureConsumer<ClearCartOnOrderConfirmedConsumer>(context);
                });
            });

        return services;
    }
}
