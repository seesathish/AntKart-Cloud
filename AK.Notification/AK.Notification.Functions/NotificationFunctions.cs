using System.Globalization;
using System.Text.Json;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Notification.Core.Channels;
using AK.Notification.Core.Dispatch;
using AK.Notification.Core.Templates;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Functions;

// Event Grid-triggered notification Functions (.NET 9 isolated worker) — the serverless half of the
// platform's eventing model. There is ONE Function per customer notification event.
//
// THE TRIGGER → DISPATCHER SEAM (why each Function is deliberately THIN):
//   A Function does only two things:
//     1. deserialize the Event Grid payload into the shared contract (AK.BuildingBlocks);
//     2. build a NotificationRequest and hand it to INotificationDispatcher.
//   ALL real work — template rendering, channel selection/sending (ACS email), history
//   persistence, and failure handling — lives in AK.Notification.Core behind the dispatcher. That
//   keeps the Functions trivial, keeps the logic reusable and unit-testable OUTSIDE Azure, and means
//   adding a channel or changing a template never touches a Function.
//
// The dispatcher NEVER throws for a delivery failure (it records it), so the Functions need no
// try/catch around dispatch — a notification stays a best-effort side-effect.
public sealed class NotificationFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<NotificationFunctions> _logger;

    public NotificationFunctions(INotificationDispatcher dispatcher, ILogger<NotificationFunctions> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    [Function(nameof(OnOrderCreated))]
    public Task OnOrderCreated([EventGridTrigger] EventGridEvent input)
    {
        if (!TryDeserialize<OrderCreatedNotification>(input, out var e)) return Task.CompletedTask;
        return DispatchAsync(NotificationType.OrderCreated, e.CustomerEmail, new Dictionary<string, string?>
        {
            [NotificationDataKeys.CustomerName] = e.CustomerName,
            [NotificationDataKeys.OrderNumber] = e.OrderNumber,
            [NotificationDataKeys.TotalAmount] = Amount(e.TotalAmount)
        });
    }

    [Function(nameof(OnOrderConfirmed))]
    public Task OnOrderConfirmed([EventGridTrigger] EventGridEvent input)
    {
        if (!TryDeserialize<OrderConfirmedNotification>(input, out var e)) return Task.CompletedTask;
        return DispatchAsync(NotificationType.OrderConfirmed, e.CustomerEmail, new Dictionary<string, string?>
        {
            [NotificationDataKeys.CustomerName] = e.CustomerName,
            [NotificationDataKeys.OrderNumber] = e.OrderNumber,
            [NotificationDataKeys.TotalAmount] = Amount(e.TotalAmount)
        });
    }

    [Function(nameof(OnOrderCancelled))]
    public Task OnOrderCancelled([EventGridTrigger] EventGridEvent input)
    {
        if (!TryDeserialize<OrderCancelledNotification>(input, out var e)) return Task.CompletedTask;
        return DispatchAsync(NotificationType.OrderCancelled, e.CustomerEmail, new Dictionary<string, string?>
        {
            [NotificationDataKeys.CustomerName] = e.CustomerName,
            [NotificationDataKeys.OrderNumber] = e.OrderNumber,
            [NotificationDataKeys.Reason] = e.Reason
        });
    }

    [Function(nameof(OnPaymentSucceeded))]
    public Task OnPaymentSucceeded([EventGridTrigger] EventGridEvent input)
    {
        if (!TryDeserialize<PaymentSucceededNotification>(input, out var e)) return Task.CompletedTask;
        return DispatchAsync(NotificationType.PaymentSucceeded, e.CustomerEmail, new Dictionary<string, string?>
        {
            [NotificationDataKeys.CustomerName] = e.CustomerName,
            [NotificationDataKeys.OrderNumber] = e.OrderNumber,
            [NotificationDataKeys.Amount] = Amount(e.Amount),
            [NotificationDataKeys.PaymentId] = e.PaymentId
        });
    }

    [Function(nameof(OnPaymentFailed))]
    public Task OnPaymentFailed([EventGridTrigger] EventGridEvent input)
    {
        if (!TryDeserialize<PaymentFailedNotification>(input, out var e)) return Task.CompletedTask;
        return DispatchAsync(NotificationType.PaymentFailed, e.CustomerEmail, new Dictionary<string, string?>
        {
            [NotificationDataKeys.CustomerName] = e.CustomerName,
            [NotificationDataKeys.OrderNumber] = e.OrderNumber,
            [NotificationDataKeys.Reason] = e.Reason
        });
    }

    private async Task DispatchAsync(NotificationType type, string recipient, Dictionary<string, string?> data)
    {
        var result = await _dispatcher.DispatchAsync(new NotificationRequest(type, recipient, data));

        // The dispatcher already recorded each attempt; just surface a one-line summary.
        if (!result.AllSucceeded)
            _logger.LogWarning("Notification {Type} to {Recipient} had channel failures.", type, recipient);
    }

    private bool TryDeserialize<T>(EventGridEvent input, out T value) where T : class
    {
        try
        {
            value = input.Data?.ToObjectFromJson<T>(JsonOptions)!;
        }
        catch (Exception ex)
        {
            // A malformed payload can't be fixed by retrying — log and skip rather than crash-loop.
            _logger.LogWarning(ex, "Event {EventType} ({Subject}) payload could not be deserialized; skipping.",
                input.EventType, input.Subject);
            value = null!;
            return false;
        }

        if (value is not null) return true;

        _logger.LogWarning("Event {EventType} ({Subject}) had no payload; skipping.",
            input.EventType, input.Subject);
        return false;
    }

    private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
