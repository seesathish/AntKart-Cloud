namespace AK.Notification.Core.Channels;

// The five customer notification types the platform sends. Each maps to exactly one template
// (resolved by this value) and is recorded on every history entry.
public enum NotificationType
{
    OrderCreated = 1,
    OrderConfirmed = 2,
    OrderCancelled = 3,
    PaymentSucceeded = 4,
    PaymentFailed = 5
}
