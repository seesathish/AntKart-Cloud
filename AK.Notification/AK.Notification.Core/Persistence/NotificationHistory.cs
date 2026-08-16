using AK.BuildingBlocks.DDD;
using AK.Notification.Core.Channels;

namespace AK.Notification.Core.Persistence;

// The terminal outcome of a single delivery attempt, as recorded in history.
public enum NotificationDeliveryStatus
{
    Sent = 1,
    Failed = 2
}

// The audit record the dispatcher writes for EVERY delivery attempt (one row per channel per
// request) — the notification system's history/audit trail. Refactored from the previous
// AK.Notification entity to the fields the serverless core needs, with a CorrelationId added.
//
// Inherits Id (Guid) and CreatedAt (the attempt timestamp) from the shared BuildingBlocks Entity.
public sealed class NotificationHistory : Entity
{
    public NotificationType NotificationType { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public NotificationChannelType ChannelType { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }

    // A business identifier that ties the notification back to its trigger (e.g. the order number).
    public string? CorrelationId { get; private set; }

    // Populated only when Status is Failed.
    public string? ErrorMessage { get; private set; }

    private NotificationHistory() { }

    public static NotificationHistory Record(
        NotificationType notificationType,
        string recipient,
        NotificationChannelType channelType,
        NotificationDeliveryStatus status,
        string? correlationId,
        string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Recipient cannot be empty.", nameof(recipient));

        return new NotificationHistory
        {
            NotificationType = notificationType,
            Recipient = recipient,
            ChannelType = channelType,
            Status = status,
            CorrelationId = correlationId,
            ErrorMessage = errorMessage
        };
    }
}
