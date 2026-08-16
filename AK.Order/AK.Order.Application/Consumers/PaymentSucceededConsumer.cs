using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

// Reacts to a successful Razorpay payment (published by AK.Payments after signature verification).
// Updates the order status to Paid and records that payment has been confirmed.
// If the order no longer exists (edge case: deleted between events), silently skip — no retry needed.
public sealed class PaymentSucceededConsumer(IUnitOfWork uow) : IConsumer<PaymentSucceededIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var order = await uow.Orders.GetByIdAsync(context.Message.OrderId, context.CancellationToken);
        if (order is null) return;

        // Move the order to Paid. UpdateStatus enforces the state machine (Order._allowedTransitions),
        // which must permit Confirmed → Paid — the order is in Confirmed by the time payment is verified.
        order.UpdateStatus(OrderStatus.Paid);
        order.ConfirmPayment();  // also sets PaymentStatus = Paid on the order
        await uow.SaveChangesAsync(context.CancellationToken);
    }
}
