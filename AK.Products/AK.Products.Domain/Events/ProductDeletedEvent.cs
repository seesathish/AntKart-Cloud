using AK.BuildingBlocks.DDD;
namespace AK.Products.Domain.Events;

public record ProductDeletedEvent(string ProductId) : IDomainEvent;
