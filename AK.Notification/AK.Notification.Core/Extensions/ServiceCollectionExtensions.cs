using AK.BuildingBlocks.Email;
using AK.Notification.Core.Channels;
using AK.Notification.Core.Dispatch;
using AK.Notification.Core.Persistence;
using AK.Notification.Core.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Notification.Core.Extensions;

public static class ServiceCollectionExtensions
{
    // Registers the entire reusable notification core: the dispatcher, the channel(s), the
    // per-type templates, the history store, and the history DbContext. Designed to be the single
    // call a host makes — the Azure Functions app will call this in the next step.
    public static IServiceCollection AddNotificationCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Email channel — wraps the shared ACS IEmailSender (managed-identity, secret-less). Adding
        // a future channel is one more AddScoped<INotificationChannel, ...> line; nothing else changes.
        services.AddAcsEmailSender(configuration);
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();

        // One template per notification type, resolved by NotificationType.
        services.AddSingleton<INotificationTemplate, OrderCreatedTemplate>();
        services.AddSingleton<INotificationTemplate, OrderConfirmedTemplate>();
        services.AddSingleton<INotificationTemplate, OrderCancelledTemplate>();
        services.AddSingleton<INotificationTemplate, PaymentSucceededTemplate>();
        services.AddSingleton<INotificationTemplate, PaymentFailedTemplate>();
        services.AddSingleton<INotificationTemplateResolver, NotificationTemplateResolver>();

        // Dispatcher + history audit trail.
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<INotificationHistoryStore, EfNotificationHistoryStore>();

        // History persistence. Secret-less, same pattern as the other services: the connection
        // string comes from configuration (ConnectionStrings:Notifications), which is sourced from
        // Key Vault in the deployed environment. Kept building with the existing config for now.
        var connectionString = configuration.GetConnectionString("Notifications")
            ?? configuration.GetConnectionString("Postgres");
        services.AddDbContext<NotificationHistoryDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
