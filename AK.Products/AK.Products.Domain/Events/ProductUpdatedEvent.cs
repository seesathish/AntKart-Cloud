using AK.BuildingBlocks.DDD;
namespace AK.Products.Domain.Events;

public record ProductUpdatedEvent(string ProductId, string ProductName) : IDomainEvent;
