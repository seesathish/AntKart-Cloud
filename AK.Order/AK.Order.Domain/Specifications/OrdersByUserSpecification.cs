using AK.Order.Domain.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Domain.Specifications;

public sealed class OrdersByUserSpecification : BaseSpecification<OrderEntity>
{
    public OrdersByUserSpecification(string userId)
        : base(o => o.UserId == userId)
    {
        ApplyOrderByDescending(o => o.CreatedAt);
    }
}
