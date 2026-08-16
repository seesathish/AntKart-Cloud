namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string UserId,
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
