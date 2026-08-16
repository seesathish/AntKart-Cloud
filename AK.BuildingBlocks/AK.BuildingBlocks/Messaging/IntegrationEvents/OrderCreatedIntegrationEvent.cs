namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string UserId,
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    IReadOnlyList<OrderItemPayload> Items,
    decimal TotalAmount) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderItemPayload(
    string ProductId,
    string Sku,
    int Quantity,
    decimal UnitPrice);
