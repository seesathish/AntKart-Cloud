using AK.Notification.Core.Channels;

namespace AK.Notification.Core.Templates;

// The composed output of a template: the subject and both body representations. Channels that
// support rich content use the HTML body; plain-text is the universal fallback.
public sealed record NotificationContent(string Subject, string HtmlBody, string PlainTextBody);

// One template per NotificationType. It turns the loose event data into a NotificationContent.
// Templates are channel-agnostic (they produce subject + bodies, not channel specifics).
public interface INotificationTemplate
{
    NotificationType NotificationType { get; }

    NotificationContent Render(IReadOnlyDictionary<string, string?> data);
}

// Resolves the template registered for a given NotificationType.
public interface INotificationTemplateResolver
{
    INotificationTemplate Resolve(NotificationType notificationType);
}
