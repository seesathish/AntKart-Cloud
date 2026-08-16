namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record PaymentSucceededIntegrationEvent(
    Guid PaymentId,
    Guid OrderId,
    string UserId,
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal Amount,
    string RazorpayPaymentId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
