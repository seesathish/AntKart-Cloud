using AK.BuildingBlocks.Common;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using MediatR;

namespace AK.Order.Application.Features.UpdateOrderStatus;

// Returns Result<OrderDto> instead of throwing for expected business outcomes.
//
// The domain's _allowedTransitions dictionary defines every valid state transition.
// When UpdateStatus() rejects a transition it throws InvalidOperationException —
// we catch that here and translate it to Result.Failure so the endpoint layer can
// return a 409 Conflict with the domain's own error message, rather than having
// the exception bubble up through ExceptionHandlerMiddleware (which would also
// produce a 409, but loses the decision about what the response body should say).
public sealed class UpdateOrderStatusCommandHandler(IUnitOfWork uow)
    : IRequestHandler<UpdateOrderStatusCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(UpdateOrderStatusCommand request, CancellationToken ct)
    {
        var order = await uow.Orders.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result<OrderDto>.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.UpdateStatus(request.NewStatus);
        }
        catch (InvalidOperationException ex)
        {
            // Domain rejected the transition (e.g. Cancelled → Processing).
            // Translate to Result.Failure so the endpoint returns 409 with the
            // domain's error message rather than relying on ExceptionHandlerMiddleware.
            return Result<OrderDto>.Failure(ex.Message);
        }

        await uow.Orders.UpdateAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        order.ClearDomainEvents();
        return Result<OrderDto>.Success(OrderMapper.ToDto(order));
    }
}
