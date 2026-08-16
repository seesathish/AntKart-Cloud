using AK.BuildingBlocks.Email;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Core.Channels;

// The Email channel — the only implemented channel today. It wraps the shared IEmailSender (Azure
// Communication Services) from AK.BuildingBlocks, so it inherits the platform's secret-less,
// managed-identity email delivery.
//
// HOW TO ADD A FUTURE CHANNEL (e.g. WhatsApp): create a class like this one with
// ChannelType => NotificationChannelType.WhatsApp, send through its provider, and register it in
// AddNotificationCore. The dispatcher and templates are untouched — that is the Open/Closed payoff.
internal sealed class EmailNotificationChannel(
    IEmailSender emailSender,
    ILogger<EmailNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public async Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        // The channel NEVER throws: a delivery error becomes a Failed result so the dispatcher can
        // record it and continue. (IEmailSender itself throws on a real send error and is a safe
        // no-op when ACS is unconfigured — both are handled here.)
        try
        {
            await emailSender.SendAsync(
                to: message.Recipient,
                subject: message.Subject,
                htmlBody: message.HtmlBody,
                plainTextBody: message.PlainTextBody,
                ct: ct);

            return NotificationSendResult.Sent();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Email delivery failed for {NotificationType} to {Recipient}: {Error}",
                message.NotificationType, message.Recipient, ex.Message);
            return NotificationSendResult.Failed(ex.Message);
        }
    }
}
