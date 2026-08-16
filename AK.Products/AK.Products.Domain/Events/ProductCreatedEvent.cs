using AK.BuildingBlocks.DDD;
namespace AK.Products.Domain.Events;

public sealed record ProductCreatedEvent(string ProductId, string ProductName) : IDomainEvent;
