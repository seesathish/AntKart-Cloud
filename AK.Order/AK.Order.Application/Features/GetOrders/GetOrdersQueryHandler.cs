using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Specifications;
using AK.BuildingBlocks.Common;
using MediatR;

namespace AK.Order.Application.Features.GetOrders;

public sealed class GetOrdersQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        var spec = new OrdersPagedSpecification(request.Page, request.PageSize, request.UserId, request.Status);
        var countSpec = new OrdersPagedSpecification(1, int.MaxValue, request.UserId, request.Status);

        var orders = await uow.Orders.ListAsync(spec, ct);
        var total = await uow.Orders.CountAsync(countSpec, ct);

        var items = orders.Select(OrderMapper.ToDto).ToList();
        return new PagedResult<OrderDto>(items, total, request.Page, request.PageSize);
    }
}
