using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.ShoppingCart.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AK.ShoppingCart.Application.Consumers;

// MassTransit consumer: runs when the SAGA publishes OrderConfirmedIntegrationEvent
// (i.e. after stock has been reserved successfully and the order is confirmed).
//
// Responsibility: clear the user's cart so they start fresh for their next purchase.
// This is fire-and-forget from the SAGA's perspective — the cart clear is best-effort
// and never blocks the order flow. If the cart is already gone (user cleared it manually,
// or Redis TTL expired), we just skip silently.
public sealed class ClearCartOnOrderConfirmedConsumer(
    IUnitOfWork uow,
    ILogger<ClearCartOnOrderConfirmedConsumer> logger) : IConsumer<OrderConfirmedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;
        logger.LogInformation("Clearing cart for UserId={UserId} after OrderId={OrderId} confirmed",
            userId, context.Message.OrderId);

        // Check before delete to avoid a Redis delete-on-missing-key log noise.
        var exists = await uow.Carts.ExistsAsync(userId, context.CancellationToken);
        if (!exists)
        {
            logger.LogInformation("No active cart found for UserId={UserId}, skipping clear", userId);
            return;
        }

        await uow.Carts.DeleteAsync(userId, context.CancellationToken);
        logger.LogInformation("Cart cleared for UserId={UserId}", userId);
    }
}
