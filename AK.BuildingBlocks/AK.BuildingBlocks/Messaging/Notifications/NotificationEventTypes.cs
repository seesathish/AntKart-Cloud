namespace AK.BuildingBlocks.Messaging.Notifications;

// The Event Grid `eventType` strings for the five customer notification events. A publisher
// (AK.Order / AK.Payments, next step) sets one of these as the EventGridEvent's EventType; the
// notification Functions route on it. Defined ONCE here so publisher and consumer never drift.
public static class NotificationEventTypes
{
    public const string OrderCreated = "AntKart.Order.Created";
    public const string OrderConfirmed = "AntKart.Order.Confirmed";
    public const string OrderCancelled = "AntKart.Order.Cancelled";
    public const string PaymentSucceeded = "AntKart.Payment.Succeeded";
    public const string PaymentFailed = "AntKart.Payment.Failed";
}
