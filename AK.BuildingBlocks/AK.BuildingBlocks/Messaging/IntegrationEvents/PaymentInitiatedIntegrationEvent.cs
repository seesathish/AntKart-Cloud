namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record PaymentInitiatedIntegrationEvent(
    Guid PaymentId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Currency,
    string RazorpayOrderId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
