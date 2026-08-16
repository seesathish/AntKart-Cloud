namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record StockReservationFailedIntegrationEvent(
    Guid OrderId,
    string UserId,
    string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
