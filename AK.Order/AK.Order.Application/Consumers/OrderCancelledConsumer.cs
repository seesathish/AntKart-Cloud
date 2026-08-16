using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

// Reacts to OrderCancelledIntegrationEvent. This consumer is the SINGLE SINK for cancellation: the
// event is published both by the customer-initiated cancel (CancelOrderCommandHandler) and by the
// saga's auto-cancel on stock-reservation failure. Emitting the notification here therefore sends
// exactly ONE OrderCancelled notification per cancellation, whichever path triggered it.
public sealed class OrderCancelledConsumer(
    IUnitOfWork uow,
    IEventGridSideEffectPublisher sideEffects) : IConsumer<OrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelledIntegrationEvent> context)
    {
        var msg = context.Message;

        var order = await uow.Orders.GetByIdAsync(msg.OrderId, context.CancellationToken);
        if (order is null) return;

        // Durable, transaction-critical step.
        order.UpdateStatus(OrderStatus.Cancelled);
        await uow.SaveChangesAsync(context.CancellationToken);

        // COMMIT-THEN-NOTIFY. Fire-and-forget Event Grid side-effect AFTER the durable commit, never
        // inside it. TryPublishAsync never throws, so a notification failure cannot fail this
        // consumer, retry the Service Bus message, or roll back the cancellation. The integration
        // event already carries the customer details, so no extra load is needed.
        await sideEffects.TryPublishAsync(
            NotificationEventTypes.OrderCancelled,
            $"orders/{msg.OrderId}",
            new OrderCancelledNotification(
                msg.CustomerEmail, msg.CustomerName, msg.OrderNumber, msg.Reason, DateTimeOffset.UtcNow),
            context.CancellationToken);
    }
}
