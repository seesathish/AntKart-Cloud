namespace AK.BuildingBlocks.Messaging.Notifications;

// The five customer notification event payloads — the SINGLE shared definition referenced by both
// the publishers (AK.Order / AK.Payments, next step) and the consumer (the notification Functions).
// Each is carried as the `data` of the Event Grid event whose EventType is the matching
// NotificationEventTypes constant.
//
// OrderNumber doubles as the CORRELATION ID that ties a notification back to its order across the
// system; OccurredAt is the event timestamp. Amounts carry their Currency.

public sealed record OrderCreatedNotification(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt);

public sealed record OrderConfirmedNotification(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt);

public sealed record OrderCancelledNotification(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record PaymentSucceededNotification(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string PaymentId,
    DateTimeOffset OccurredAt);

public sealed record PaymentFailedNotification(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    string Reason,
    DateTimeOffset OccurredAt);
