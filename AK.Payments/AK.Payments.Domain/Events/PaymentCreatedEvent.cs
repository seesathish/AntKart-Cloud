using AK.BuildingBlocks.DDD;

namespace AK.Payments.Domain.Events;

public sealed record PaymentCreatedEvent(Guid PaymentId, Guid OrderId, string UserId) : IDomainEvent;
