using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Messaging.EventGrid;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.Consumers;
using AK.Payments.Infrastructure.External;
using AK.Payments.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Payments.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("PaymentsDb")
            ?? throw new InvalidOperationException("Connection string 'PaymentsDb' is missing.");

        services.AddDbContext<PaymentsDbContext>(opts => opts.UseNpgsql(connStr));

        services.Configure<RazorpaySettings>(configuration.GetSection("Razorpay"));
        services.AddHttpClient("razorpay");
        services.AddScoped<IRazorpayClient, RazorpayGatewayClient>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Fire-and-forget Event Grid publisher for the payment notification side-effects
        // (PaymentSucceeded / PaymentFailed). Reads the non-secret EventGrid:TopicEndpoint and
        // authenticates with DefaultAzureCredential (managed identity) — never-throws TryPublishAsync.
        services.AddEventGridSideEffectPublisher();

        services.AddAzureServiceBusMassTransit(
            configuration,
            x =>
            {
                x.AddEntityFrameworkOutbox<PaymentsDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });

                x.AddConsumer<OrderConfirmedConsumer>();
            },
            // Bind the provisioned "payments" subscription on the integration-events topic.
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("payments", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureConsumer<OrderConfirmedConsumer>(context);
                });
            });

        return services;
    }
}
