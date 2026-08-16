using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Resilience;
using AK.Order.Application.Consumers;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Sagas;
using AK.Order.Infrastructure.Catalog;
using AK.Order.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Order.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        services.AddDbContext<OrderDbContext>(opts =>
            opts.UseNpgsql(connStr));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddNpgsqlResilience();

        // Typed HttpClient to AK.Products for authoritative price verification at order creation.
        // The Products read endpoint is AllowAnonymous, so no token handler is needed. Pricing is a
        // CRITICAL dependency here (we fail closed if it's down), so it uses the patient
        // retry → circuit-breaker → timeout pipeline. HttpClient.Timeout is set to InfiniteTimeSpan
        // so the message-handler timeout does not fight the resilience pipeline's own total-timeout
        // (the resilience handler governs per-attempt and overall timing).
        var productsBaseUrl = configuration["ProductsApi:BaseUrl"]
            ?? throw new InvalidOperationException("Configuration 'ProductsApi:BaseUrl' is missing.");

        services.AddHttpClient<ICatalogPriceProvider, HttpCatalogPriceProvider>(client =>
        {
            client.BaseAddress = new Uri(productsBaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddHttpResilienceWithCircuitBreaker();

        // Fire-and-forget Event Grid side-effect publisher (notifications), decoupled from the
        // durable Service Bus saga. Authenticates via DefaultAzureCredential (no key); reads the
        // non-secret EventGrid:TopicEndpoint setting.
        services.AddEventGridSideEffectPublisher();

        services.AddAzureServiceBusMassTransit(
            configuration,
            // Register the saga (with its EF repository), the outbox, and the event consumers.
            x =>
            {
                x.AddSagaStateMachine<OrderSaga, OrderSagaState>()
                 .EntityFrameworkRepository(r =>
                 {
                     r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                     r.ExistingDbContext<OrderDbContext>();
                     r.UsePostgres();
                 });

                x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });

                x.AddConsumer<OrderConfirmedConsumer>();
                x.AddConsumer<OrderCancelledConsumer>();
                x.AddConsumer<PaymentSucceededConsumer>();
                x.AddConsumer<PaymentFailedConsumer>();
            },
            // Bind to the provisioned "order" subscription on the integration-events topic. The
            // saga and the event consumers all consume PUBLISHED integration events, so they read
            // from this subscription. (The provisioned order-commands queue is reserved for
            // command-style messaging; the current order workflow is event-driven.)
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("order", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureSaga<OrderSagaState>(context);
                    e.ConfigureConsumer<OrderConfirmedConsumer>(context);
                    e.ConfigureConsumer<OrderCancelledConsumer>(context);
                    e.ConfigureConsumer<PaymentSucceededConsumer>(context);
                    e.ConfigureConsumer<PaymentFailedConsumer>(context);
                });
            });

        return services;
    }
}
