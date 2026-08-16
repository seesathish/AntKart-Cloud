using AK.BuildingBlocks.Messaging.Notifications;
using AK.Notification.Core.Channels;
using AK.Notification.Core.Dispatch;
using AK.Notification.Core.Templates;
using AK.Notification.Functions;
using Azure.Messaging.EventGrid;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AK.Notification.Tests.Functions;

public sealed class NotificationFunctionsTests
{
    private readonly Mock<INotificationDispatcher> _dispatcher = new();
    private NotificationRequest? _captured;
    private readonly NotificationFunctions _functions;

    public NotificationFunctionsTests()
    {
        _dispatcher
            .Setup(d => d.DispatchAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((r, _) => _captured = r)
            .ReturnsAsync(new NotificationDispatchResult(
                [new NotificationChannelResult(NotificationChannelType.Email, NotificationSendResult.Sent())]));

        _functions = new NotificationFunctions(_dispatcher.Object, NullLogger<NotificationFunctions>.Instance);
    }

    private static EventGridEvent Event(string eventType, object data) =>
        new("orders/ORD-1", eventType, "1.0", BinaryData.FromObjectAsJson(data));

    [Fact]
    public async Task OnOrderCreated_DeserializesAndDispatchesOrderCreatedRequest()
    {
        var payload = new OrderCreatedNotification("alice@example.com", "Alice", "ORD-1", 59.98m, "USD", DateTimeOffset.UtcNow);

        await _functions.OnOrderCreated(Event(NotificationEventTypes.OrderCreated, payload));

        _captured.Should().NotBeNull();
        _captured!.NotificationType.Should().Be(NotificationType.OrderCreated);
        _captured.Recipient.Should().Be("alice@example.com");
        _captured.Data[NotificationDataKeys.CustomerName].Should().Be("Alice");
        _captured.Data[NotificationDataKeys.OrderNumber].Should().Be("ORD-1");
        _captured.Data[NotificationDataKeys.TotalAmount].Should().Be("59.98");
    }

    [Fact]
    public async Task OnOrderConfirmed_DispatchesOrderConfirmedRequest()
    {
        var payload = new OrderConfirmedNotification("alice@example.com", "Alice", "ORD-1", 59.98m, "USD", DateTimeOffset.UtcNow);

        await _functions.OnOrderConfirmed(Event(NotificationEventTypes.OrderConfirmed, payload));

        _captured!.NotificationType.Should().Be(NotificationType.OrderConfirmed);
        _captured.Data[NotificationDataKeys.OrderNumber].Should().Be("ORD-1");
    }

    [Fact]
    public async Task OnOrderCancelled_DispatchesWithReason()
    {
        var payload = new OrderCancelledNotification("alice@example.com", "Alice", "ORD-1", "Out of stock", DateTimeOffset.UtcNow);

        await _functions.OnOrderCancelled(Event(NotificationEventTypes.OrderCancelled, payload));

        _captured!.NotificationType.Should().Be(NotificationType.OrderCancelled);
        _captured.Data[NotificationDataKeys.Reason].Should().Be("Out of stock");
    }

    [Fact]
    public async Task OnPaymentSucceeded_DispatchesWithAmountAndPaymentId()
    {
        var payload = new PaymentSucceededNotification("alice@example.com", "Alice", "ORD-1", 59.98m, "USD", "pay_ABC", DateTimeOffset.UtcNow);

        await _functions.OnPaymentSucceeded(Event(NotificationEventTypes.PaymentSucceeded, payload));

        _captured!.NotificationType.Should().Be(NotificationType.PaymentSucceeded);
        _captured.Data[NotificationDataKeys.Amount].Should().Be("59.98");
        _captured.Data[NotificationDataKeys.PaymentId].Should().Be("pay_ABC");
    }

    [Fact]
    public async Task OnPaymentFailed_DispatchesWithReason()
    {
        var payload = new PaymentFailedNotification("alice@example.com", "Alice", "ORD-1", "Card declined", DateTimeOffset.UtcNow);

        await _functions.OnPaymentFailed(Event(NotificationEventTypes.PaymentFailed, payload));

        _captured!.NotificationType.Should().Be(NotificationType.PaymentFailed);
        _captured.Data[NotificationDataKeys.Reason].Should().Be("Card declined");
    }

    [Fact]
    public async Task EmptyPayload_DoesNotDispatch()
    {
        // An event with no data must not dispatch (and must not throw).
        var empty = new EventGridEvent("orders/ORD-1", NotificationEventTypes.OrderCreated, "1.0", BinaryData.FromString(""));

        await _functions.OnOrderCreated(empty);

        _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
