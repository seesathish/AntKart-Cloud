using AK.Order.Domain.Common;
using AK.Order.Domain.Enums;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Domain.Specifications;

public sealed class OrdersByStatusSpecification : BaseSpecification<OrderEntity>
{
    public OrdersByStatusSpecification(OrderStatus status)
        : base(o => o.Status == status)
    {
        ApplyOrderByDescending(o => o.CreatedAt);
    }
}
