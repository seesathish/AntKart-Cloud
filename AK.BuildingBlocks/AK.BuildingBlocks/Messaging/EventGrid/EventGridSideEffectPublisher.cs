using Azure.Identity;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AK.BuildingBlocks.Messaging.EventGrid;

// Event Grid implementation of the fire-and-forget side-effect publisher.
//
// ── Entra authentication, no key ────────────────────────────────────────────────────────────
// The client publishes with Microsoft Entra (DefaultAzureCredential): the developer's Azure CLI
// sign-in locally and the resource's managed identity in the cloud. The identity is granted the
// "EventGrid Data Sender" role on the topic. NO topic access key / SAS is used — the only setting
// is the topic's publish ENDPOINT, which is non-secret and committed.
//
// ── Decoupling guarantee ────────────────────────────────────────────────────────────────────
// TryPublishAsync NEVER throws: a publish failure (or a missing/invalid endpoint) is swallowed and
// logged, returning false. This is what keeps the side-effect path from ever failing, delaying, or
// rolling back the durable Service Bus transaction that triggered it.
public sealed class EventGridSideEffectPublisher : IEventGridSideEffectPublisher
{
    private readonly EventGridPublisherClient? _client;
    private readonly ILogger<EventGridSideEffectPublisher> _logger;

    public EventGridSideEffectPublisher(
        IConfiguration configuration,
        ILogger<EventGridSideEffectPublisher> logger)
    {
        _logger = logger;

        // Non-secret topic publish endpoint, e.g. https://evgt-antkart-dev.<region>-1.eventgrid.azure.net/api/events.
        // If it is missing or not a valid absolute URI (e.g. offline development, not-yet-configured),
        // the publisher is a no-op — side-effects are simply not emitted, and nothing fails.
        var endpoint = configuration["EventGrid:TopicEndpoint"];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var topicUri))
            _client = new EventGridPublisherClient(topicUri, new DefaultAzureCredential());
        else
            _logger.LogInformation(
                "Event Grid side-effect publishing is disabled: no valid EventGrid:TopicEndpoint configured.");
    }

    public async Task<bool> TryPublishAsync(
        string eventType, string subject, object data, CancellationToken ct = default)
    {
        if (_client is null)
            return false;

        try
        {
            // EventGridEvent(subject, eventType, dataVersion, data) — a custom event on the topic.
            var gridEvent = new EventGridEvent(subject, eventType, "1.0", data);
            await _client.SendEventAsync(gridEvent, ct);
            return true;
        }
        catch (Exception ex)
        {
            // Decoupling guarantee: a side-effect publish failure must NEVER reach the caller's
            // transaction. Log a warning and carry on. (No secret is ever logged.)
            _logger.LogWarning(ex,
                "Event Grid side-effect publish failed for event {EventType}, subject {Subject}; the core transaction is unaffected.",
                eventType, subject);
            return false;
        }
    }
}

public static class EventGridExtensions
{
    // Registers the fire-and-forget Event Grid side-effect publisher. Reads the non-secret
    // EventGrid:TopicEndpoint setting; authentication is via DefaultAzureCredential (no key).
    public static IServiceCollection AddEventGridSideEffectPublisher(this IServiceCollection services)
    {
        services.AddSingleton<IEventGridSideEffectPublisher, EventGridSideEffectPublisher>();
        return services;
    }
}
