using AK.BuildingBlocks.DDD;

namespace AK.Payments.Domain.Events;

public sealed record PaymentFailedEvent(Guid PaymentId, Guid OrderId, string Reason) : IDomainEvent;
