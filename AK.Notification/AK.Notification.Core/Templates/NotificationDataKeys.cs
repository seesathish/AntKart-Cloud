namespace AK.Notification.Core.Templates;

// The well-known keys a NotificationRequest.Data map may carry. Using named constants keeps the
// producer (e.g. the Function deserialising an Event Grid payload) and the templates in agreement
// without a rigid typed contract — the data stays loose and serverless-friendly.
public static class NotificationDataKeys
{
    public const string CustomerName = "CustomerName";
    public const string OrderNumber = "OrderNumber";
    public const string TotalAmount = "TotalAmount";
    public const string Amount = "Amount";
    public const string Reason = "Reason";
    public const string PaymentId = "PaymentId";
}

internal static class NotificationDataExtensions
{
    // Reads a value or returns a fallback — templates never NRE on a missing/blank field.
    public static string GetOrDefault(
        this IReadOnlyDictionary<string, string?> data, string key, string fallback = "") =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value! : fallback;
}
