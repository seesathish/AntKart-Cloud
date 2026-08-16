using AK.Order.Application.Common.DTOs;
using AK.BuildingBlocks.Common;
using MediatR;

namespace AK.Order.Application.Features.GetOrdersByUser;

public sealed record GetOrdersByUserQuery(
    string UserId,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<OrderDto>>;
