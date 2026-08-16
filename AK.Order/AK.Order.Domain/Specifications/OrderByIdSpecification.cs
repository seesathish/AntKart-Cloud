using AK.Order.Domain.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Domain.Specifications;

public sealed class OrderByIdSpecification : BaseSpecification<OrderEntity>
{
    public OrderByIdSpecification(Guid id)
        : base(o => o.Id == id) { }
}
