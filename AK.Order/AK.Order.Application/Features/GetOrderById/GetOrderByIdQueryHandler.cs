using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using MediatR;

namespace AK.Order.Application.Features.GetOrderById;

public sealed class GetOrderByIdQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await uow.Orders.GetByIdAsync(request.OrderId, ct);
        return order is null ? null : OrderMapper.ToDto(order);
    }
}
