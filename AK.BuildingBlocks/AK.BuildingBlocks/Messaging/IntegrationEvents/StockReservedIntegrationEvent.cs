namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record StockReservedIntegrationEvent(
    Guid OrderId,
    string UserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
