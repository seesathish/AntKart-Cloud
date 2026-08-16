using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Enums;
using AK.BuildingBlocks.Common;
using MediatR;

namespace AK.Order.Application.Features.GetOrders;

public sealed record GetOrdersQuery(
    int Page = 1,
    int PageSize = 20,
    string? UserId = null,
    OrderStatus? Status = null) : IRequest<PagedResult<OrderDto>>;
