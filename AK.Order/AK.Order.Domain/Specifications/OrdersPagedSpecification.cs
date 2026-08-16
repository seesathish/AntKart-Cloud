using AK.Order.Domain.Common;
using AK.Order.Domain.Enums;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Domain.Specifications;

public sealed class OrdersPagedSpecification : BaseSpecification<OrderEntity>
{
    public OrdersPagedSpecification(int page, int pageSize, string? userId = null, OrderStatus? status = null)
        : base(o =>
            (userId == null || o.UserId == userId) &&
            (status == null || o.Status == status))
    {
        ApplyOrderByDescending(o => o.CreatedAt);
        ApplyPaging((page - 1) * pageSize, pageSize);
    }
}
