using AK.Order.Application.Common.DTOs;
using MediatR;

namespace AK.Order.Application.Features.GetOrderById;

public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto?>;
