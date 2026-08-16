using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

// Reacts to OrderConfirmedIntegrationEvent (published by the saga over Service Bus):
//   1. DURABLE step — update the order's status to Confirmed and persist it. This is part of the
//      transactional saga backbone.
//   2. FIRE-AND-FORGET side-effect — emit a notification event to Event Grid. This is deliberately
//      SEPARATE from the saga: a serverless Function (scale-to-zero, billed per execution) handles
//      it. The publish is decoupled — TryPublishAsync never throws — so a failure to emit the
//      notification can never fail this consumer, retry the Service Bus message, or roll back the
//      order confirmation.
public sealed class OrderConfirmedConsumer(
    IUnitOfWork uow,
    IEventGridSideEffectPublisher sideEffects) : IConsumer<OrderConfirmedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context)
    {
        var order = await uow.Orders.GetByIdAsync(context.Message.OrderId, context.CancellationToken);
        if (order is null) return;

        // Durable, transaction-critical step (Service Bus saga backbone).
        order.UpdateStatus(OrderStatus.Confirmed);
        await uow.SaveChangesAsync(context.CancellationToken);

        // Fire-and-forget notification side-effect (Event Grid + Function). Runs AFTER the durable
        // commit and cannot affect it: TryPublishAsync swallows any failure and returns false.
        // Uses the SHARED NotificationEvents.OrderConfirmed contract (no ad-hoc event shape), so the
        // publisher and the consuming Function agree on one definition.
        await sideEffects.TryPublishAsync(
            NotificationEventTypes.OrderConfirmed,
            $"orders/{context.Message.OrderId}",
            new OrderConfirmedNotification(
                context.Message.CustomerEmail,
                context.Message.CustomerName,
                context.Message.OrderNumber,
                context.Message.TotalAmount,
                "USD",
                DateTimeOffset.UtcNow),
            context.CancellationToken);
    }
}
