using AK.Notification.Core.Channels;

namespace AK.Notification.Core.Templates;

// Resolves the template for a NotificationType from the set registered in DI. Building the lookup
// once (in the constructor) means an unknown/duplicate type is caught at startup, not per-send.
internal sealed class NotificationTemplateResolver : INotificationTemplateResolver
{
    private readonly IReadOnlyDictionary<NotificationType, INotificationTemplate> _templates;

    public NotificationTemplateResolver(IEnumerable<INotificationTemplate> templates)
        => _templates = templates.ToDictionary(t => t.NotificationType);

    public INotificationTemplate Resolve(NotificationType notificationType) =>
        _templates.TryGetValue(notificationType, out var template)
            ? template
            : throw new InvalidOperationException($"No notification template registered for {notificationType}.");
}
