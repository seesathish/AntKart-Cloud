using AK.BuildingBlocks.Common;
using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Enums;
using MediatR;

namespace AK.Order.Application.Features.UpdateOrderStatus;

public sealed record UpdateOrderStatusCommand(Guid OrderId, OrderStatus NewStatus) : IRequest<Result<OrderDto>>;
