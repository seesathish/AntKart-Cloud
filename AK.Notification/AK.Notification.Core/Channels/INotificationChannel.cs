namespace AK.Notification.Core.Channels;

// The result of attempting to deliver one message on one channel. A channel NEVER throws for a
// delivery failure — it returns Failed(...) so the dispatcher can record the outcome and carry on.
public sealed record NotificationSendResult(bool Success, string? Error = null)
{
    public static NotificationSendResult Sent() => new(true);
    public static NotificationSendResult Failed(string error) => new(false, error);
}

// A delivery channel (Email today; WhatsApp/Sms later). This is the extension point: a new channel
// is a new INotificationChannel implementation whose ChannelType identifies it — the dispatcher
// resolves and calls it by type, so no existing code changes (Open/Closed).
public interface INotificationChannel
{
    NotificationChannelType ChannelType { get; }

    Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken ct = default);
}
