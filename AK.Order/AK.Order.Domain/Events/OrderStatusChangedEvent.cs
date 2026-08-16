using AK.BuildingBlocks.DDD;
using AK.Order.Domain.Enums;

namespace AK.Order.Domain.Events;

public sealed record OrderStatusChangedEvent(Guid OrderId, OrderStatus OldStatus, OrderStatus NewStatus) : IDomainEvent;
