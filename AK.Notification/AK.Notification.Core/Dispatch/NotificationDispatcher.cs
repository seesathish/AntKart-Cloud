using AK.Notification.Core.Channels;
using AK.Notification.Core.Persistence;
using AK.Notification.Core.Templates;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Core.Dispatch;

// The reusable notification core's orchestrator. For one request it:
//   1. resolves the template for the NotificationType and composes the subject + bodies;
//   2. for each target channel (default: Email), builds the channel-ready NotificationMessage;
//   3. sends via the resolved channel;
//   4. writes ONE history record per attempt (Sent or Failed);
//   5. returns the aggregate result.
//
// IT NEVER THROWS FOR A DELIVERY FAILURE. A channel returns a Failed result (and even if a channel
// or the history write misbehaves, it is caught) — every attempt is recorded and reported. This is
// what lets the serverless caller treat notification as a best-effort side-effect that can never
// fault the originating transaction.
internal sealed class NotificationDispatcher : INotificationDispatcher
{
    private static readonly IReadOnlyCollection<NotificationChannelType> DefaultChannels =
        new[] { NotificationChannelType.Email };

    private readonly INotificationTemplateResolver _templates;
    private readonly IReadOnlyDictionary<NotificationChannelType, INotificationChannel> _channels;
    private readonly INotificationHistoryStore _history;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INotificationTemplateResolver templates,
        IEnumerable<INotificationChannel> channels,
        INotificationHistoryStore history,
        ILogger<NotificationDispatcher> logger)
    {
        _templates = templates;
        // Index the registered channels by type so a request is routed without any if/switch — a
        // newly registered channel is picked up automatically (Open/Closed).
        _channels = channels.ToDictionary(c => c.ChannelType);
        _history = history;
        _logger = logger;
    }

    public async Task<NotificationDispatchResult> DispatchAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var content = _templates.Resolve(request.NotificationType).Render(request.Data);
        var correlationId = request.Data.GetOrDefault(NotificationDataKeys.OrderNumber);
        var targetChannels = request.Channels is { Count: > 0 } ? request.Channels : DefaultChannels;

        var results = new List<NotificationChannelResult>(targetChannels.Count);

        foreach (var channelType in targetChannels)
        {
            var result = await SendOnChannelAsync(channelType, request, content, correlationId, ct);
            results.Add(new NotificationChannelResult(channelType, result));

            await RecordHistoryAsync(request, channelType, result, correlationId, ct);
        }

        return new NotificationDispatchResult(results);
    }

    private async Task<NotificationSendResult> SendOnChannelAsync(
        NotificationChannelType channelType, NotificationRequest request,
        NotificationContent content, string? correlationId, CancellationToken ct)
    {
        if (!_channels.TryGetValue(channelType, out var channel))
        {
            // A requested channel that isn't registered (e.g. WhatsApp before it ships) is a
            // recorded failure, not an exception — the rest of the dispatch still proceeds.
            _logger.LogWarning("No notification channel registered for {ChannelType}.", channelType);
            return NotificationSendResult.Failed($"No channel registered for {channelType}.");
        }

        var message = new NotificationMessage(
            Recipient: request.Recipient,
            Subject: content.Subject,
            HtmlBody: content.HtmlBody,
            PlainTextBody: content.PlainTextBody,
            ChannelType: channelType,
            NotificationType: request.NotificationType,
            CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? null : correlationId);

        // The channel contract is never-throw, but guard anyway so one misbehaving channel can't
        // break the dispatch or skip the history record.
        try
        {
            return await channel.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Channel {ChannelType} threw sending {NotificationType}: {Error}",
                channelType, request.NotificationType, ex.Message);
            return NotificationSendResult.Failed(ex.Message);
        }
    }

    private async Task RecordHistoryAsync(
        NotificationRequest request, NotificationChannelType channelType,
        NotificationSendResult result, string? correlationId, CancellationToken ct)
    {
        try
        {
            var record = NotificationHistory.Record(
                request.NotificationType,
                request.Recipient,
                channelType,
                result.Success ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Failed,
                string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
                result.Error);

            await _history.AddAsync(record, ct);
        }
        catch (Exception ex)
        {
            // Persisting the audit row must not fault the dispatch either; log and move on.
            _logger.LogError(
                "Failed to persist notification history for {NotificationType}/{ChannelType}: {Error}",
                request.NotificationType, channelType, ex.Message);
        }
    }
}
