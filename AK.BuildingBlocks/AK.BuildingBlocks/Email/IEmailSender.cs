namespace AK.BuildingBlocks.Email;

// Reusable email-sending abstraction used by the notification paths (the Event Grid-triggered
// Function and the AK.Notification consumers). Callers depend on this interface, not on any
// particular provider, so the delivery mechanism (currently Azure Communication Services) can
// change without touching the notification logic.
//
// At least one of htmlBody / plainTextBody must be non-empty.
public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string? htmlBody,
        string? plainTextBody = null,
        CancellationToken ct = default);
}
