namespace AK.Notification.Core.Channels;

// A fully-composed, channel-ready message: the template has already produced the subject and
// bodies. This is what a channel sends.
public sealed record NotificationMessage(
    string Recipient,
    string Subject,
    string HtmlBody,
    string PlainTextBody,
    NotificationChannelType ChannelType,
    NotificationType NotificationType,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

// The caller's request to notify someone. It carries the EVENT DATA as a loose key/value map
// (serverless-friendly — an Event Grid payload deserialises straight into it; see
// NotificationDataKeys for the well-known keys), and the target channels (default: Email).
// The dispatcher resolves the template for NotificationType, composes the message, and sends it on
// each requested channel.
public sealed record NotificationRequest(
    NotificationType NotificationType,
    string Recipient,
    IReadOnlyDictionary<string, string?> Data,
    IReadOnlyCollection<NotificationChannelType>? Channels = null);
