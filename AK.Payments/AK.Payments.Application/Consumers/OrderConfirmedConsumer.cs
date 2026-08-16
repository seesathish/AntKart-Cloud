using AK.BuildingBlocks.Messaging.IntegrationEvents;
using MassTransit;

namespace AK.Payments.Application.Consumers;

// Intentional no-op consumer: AK.Payments must be subscribed to OrderConfirmedIntegrationEvent
// so the consumer is bound to its Service Bus subscription and the message is not dropped.
// Currently no action is needed when an order is confirmed — payment is initiated separately
// when the frontend calls POST /api/payments/initiate after seeing the confirmed order.
// This stub is here as a placeholder if future logic is needed (e.g. auto-charging saved cards).
public sealed class OrderConfirmedConsumer : IConsumer<OrderConfirmedIntegrationEvent>
{
    public Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context) => Task.CompletedTask;
}
