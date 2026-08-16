using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Products.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AK.Products.Application.Consumers;

// MassTransit consumer: runs when AK.Order publishes OrderCreatedIntegrationEvent.
// This is AK.Products' role in the SAGA — attempt to reserve stock for all ordered items.
//
// Two-phase approach:
//   Phase 1 (validate): check all items before touching any inventory.
//                       If any item has insufficient stock, fail immediately with a list of SKUs.
//   Phase 2 (apply):    only decrement stock if ALL items can be fulfilled.
//                       This prevents a partial reservation (e.g. 2 of 3 items reserved).
//
// Outcome:
//   Success → publishes StockReservedIntegrationEvent → SAGA transitions to Confirmed
//   Failure → publishes StockReservationFailedIntegrationEvent → SAGA cancels the order
public sealed class ReserveStockConsumer(
    IUnitOfWork uow,
    ILogger<ReserveStockConsumer> logger) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Reserving stock for OrderId={OrderId}", msg.OrderId);

        // Phase 1: validate all items first — collect all failures before acting.
        var insufficientItems = new List<string>();
        foreach (var item in msg.Items)
        {
            var product = await uow.Products.GetByIdAsync(item.ProductId, context.CancellationToken);
            if (product is null || product.StockQuantity < item.Quantity)
                insufficientItems.Add(item.Sku);
        }

        if (insufficientItems.Count > 0)
        {
            var reason = $"Insufficient stock for: {string.Join(", ", insufficientItems)}";
            logger.LogWarning("Stock reservation failed for OrderId={OrderId}. {Reason}", msg.OrderId, reason);

            // Notify the SAGA that reservation failed — the SAGA will cancel the order.
            await context.Publish(new StockReservationFailedIntegrationEvent(msg.OrderId, msg.UserId, reason));
            return;
        }

        // Phase 2: all items are available — apply the decrements and persist.
        foreach (var item in msg.Items)
        {
            var product = await uow.Products.GetByIdAsync(item.ProductId, context.CancellationToken);
            if (product is null) continue;
            product.DecrementStock(item.Quantity);  // throws if race condition leaves stock insufficient
            await uow.Products.UpdateAsync(product, context.CancellationToken);
        }
        await uow.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Stock reserved for OrderId={OrderId}", msg.OrderId);

        // Notify the SAGA that stock was reserved — the SAGA will confirm the order.
        await context.Publish(new StockReservedIntegrationEvent(msg.OrderId, msg.UserId));
    }
}
