using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

// Reacts to a failed Razorpay payment (published by AK.Payments when signature verification fails
// or the user cancels the payment widget).
// Marks the order as PaymentFailed — the customer can retry payment or the order can be cancelled.
public sealed class PaymentFailedConsumer(IUnitOfWork uow) : IConsumer<PaymentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var order = await uow.Orders.GetByIdAsync(context.Message.OrderId, context.CancellationToken);
        if (order is null) return;

        // Move the order to PaymentFailed. UpdateStatus enforces the state machine
        // (Order._allowedTransitions), which must permit Confirmed → PaymentFailed — the order is
        // in Confirmed by the time a payment outcome is processed.
        order.UpdateStatus(OrderStatus.PaymentFailed);
        await uow.SaveChangesAsync(context.CancellationToken);
    }
}
