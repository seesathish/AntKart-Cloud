using AK.BuildingBlocks.Common;
using MediatR;

namespace AK.Order.Application.Features.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId) : IRequest<Result<bool>>;
