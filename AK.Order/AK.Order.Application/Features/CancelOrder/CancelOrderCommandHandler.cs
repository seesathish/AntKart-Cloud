using AK.BuildingBlocks.Common;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using MassTransit;
using MediatR;

namespace AK.Order.Application.Features.CancelOrder;

// Returns Result<bool> instead of throwing for expected business outcomes.
//
// "Order not found" and "order already cancelled" are EXPECTED failures — they happen
// because of valid user actions (cancelling a wrong ID, double-clicking cancel).
// Throwing exceptions for these cases forces ExceptionHandlerMiddleware to decide
// what HTTP status they map to, which is the wrong layer for a business decision.
// Returning Result.Failure() keeps the HTTP mapping decision in the endpoint layer,
// where it belongs.
//
// Genuinely unexpected failures (DB down, serialisation error) still throw and
// are caught by ExceptionHandlerMiddleware as 500 — that's the correct place for those.
//
// Contrast with CreateOrderCommandHandler, which still throws — deliberate for the
// Medium CQRS article that shows both approaches side by side.
public sealed class CancelOrderCommandHandler(IUnitOfWork uow, IPublishEndpoint publisher)
    : IRequestHandler<CancelOrderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await uow.Orders.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result<bool>.Failure($"Order {request.OrderId} not found.");

        if (order.Status == Domain.Enums.OrderStatus.Cancelled)
            return Result<bool>.Failure("Order is already cancelled.");

        if (order.Status == Domain.Enums.OrderStatus.Delivered)
            return Result<bool>.Failure("Cannot cancel a delivered order.");

        order.Cancel();
        await uow.Orders.UpdateAsync(order, ct);

        await publisher.Publish(new OrderCancelledIntegrationEvent(
            order.Id,
            order.UserId,
            order.CustomerEmail,
            order.CustomerName,
            order.OrderNumber,
            "Cancelled by customer"), ct);

        await uow.SaveChangesAsync(ct);
        order.ClearDomainEvents();
        return Result<bool>.Success(true);
    }
}
