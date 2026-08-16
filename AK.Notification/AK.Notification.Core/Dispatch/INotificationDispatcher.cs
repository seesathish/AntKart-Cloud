using AK.Notification.Core.Channels;

namespace AK.Notification.Core.Dispatch;

// The per-channel outcome of a dispatch.
public sealed record NotificationChannelResult(NotificationChannelType ChannelType, NotificationSendResult Result);

// The aggregate outcome of dispatching one request across its target channels.
public sealed record NotificationDispatchResult(IReadOnlyList<NotificationChannelResult> ChannelResults)
{
    public bool AllSucceeded => ChannelResults.Count > 0 && ChannelResults.All(r => r.Result.Success);
}

// Entry point the callers (next step: the Azure Functions) use. Resolve template → compose message
// → send on each target channel → record a history row per attempt. Never throws for a delivery
// failure; the failure is recorded and returned.
public interface INotificationDispatcher
{
    Task<NotificationDispatchResult> DispatchAsync(NotificationRequest request, CancellationToken ct = default);
}
