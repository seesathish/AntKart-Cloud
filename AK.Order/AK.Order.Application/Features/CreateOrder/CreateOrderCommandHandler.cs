using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Exceptions;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Entities;
using AK.Order.Domain.ValueObjects;
using MassTransit;
using MediatR;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Application.Features.CreateOrder;

// Handles order creation: revalidates pricing against the catalogue (server-authoritative), builds
// the domain aggregate at the EFFECTIVE price, persists it, and kicks off the SAGA by publishing
// OrderCreatedIntegrationEvent to the message bus.
//
// PRICING POLICY (asymmetric, server-authoritative): the order is always priced at the catalogue's
// effective price (DiscountPrice ?? Price). The client's submitted price is ADVISORY — used only to
// decide whether to interrupt the customer:
//   * effective price INCREASED vs submitted  → stop and ask the customer to confirm (PriceChanged);
//   * effective price equal or LOWER (a drop)  → accept silently and charge the lower price.
// A not-found / inactive product cannot be ordered (ProductUnavailable). If the catalogue can't be
// reached, the flow FAILS CLOSED (PricingUnavailable) and persists nothing.
//
// Security: UserId, CustomerEmail, and CustomerName come from the command (injected from JWT
// at the endpoint layer) — never from the client request body.
//
// SAGA trigger: publishing OrderCreatedIntegrationEvent starts the OrderSaga in AK.Order,
// which waits for AK.Products to confirm stock reservation before confirming or cancelling.
public sealed class CreateOrderCommandHandler(
    IUnitOfWork uow,
    IPublishEndpoint publisher,
    IEventGridSideEffectPublisher sideEffects,
    ICatalogPriceProvider catalog)
    : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    // Order totals are in the catalogue currency (USD); the Order aggregate doesn't store a currency.
    private const string OrderCurrency = "USD";

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // --- Authoritative price + discount revalidation, BEFORE building or persisting anything ---
        var productIds = request.Order.Items.Select(i => i.ProductId).Distinct().ToList();

        IReadOnlyDictionary<string, CatalogPriceResult> prices;
        try
        {
            prices = await catalog.GetEffectivePricesAsync(productIds, ct);
        }
        catch (CatalogUnavailableException)
        {
            // Fail closed: never price an order from an unverified source.
            return CreateOrderResult.PricingUnavailable();
        }

        var unavailable = new List<PriceProblem>();
        var priceChanged = new List<PriceProblem>();
        // Per line, the price the order will actually use (the catalogue effective price).
        var effectivePrices = new Dictionary<string, decimal>();

        foreach (var item in request.Order.Items)
        {
            if (!prices.TryGetValue(item.ProductId, out var p) || p.Status == CatalogPriceStatus.NotFound)
            {
                unavailable.Add(new PriceProblem(item.ProductId, item.Price, null, "ProductNotFound"));
            }
            else if (p.Status == CatalogPriceStatus.Inactive)
            {
                unavailable.Add(new PriceProblem(item.ProductId, item.Price, p.EffectivePrice, "ProductInactive"));
            }
            else if (p.EffectivePrice > item.Price)
            {
                // Only an INCREASE interrupts the customer.
                priceChanged.Add(new PriceProblem(item.ProductId, item.Price, p.EffectivePrice, "PriceIncreased"));
            }
            else
            {
                // Equal or a price DROP: accept and use the (authoritative) effective price.
                effectivePrices[item.ProductId] = p.EffectivePrice;
            }
        }

        // Unavailable products take precedence, then price increases. Persist nothing in either case.
        if (unavailable.Count > 0) return CreateOrderResult.ProductUnavailable(unavailable);
        if (priceChanged.Count > 0) return CreateOrderResult.PriceChanged(priceChanged);

        var addr = request.Order.ShippingAddress;

        // ShippingAddress is a Value Object — created via factory which validates required fields.
        var shippingAddress = ShippingAddress.Create(
            addr.FullName, addr.AddressLine1, addr.AddressLine2,
            addr.City, addr.State, addr.PostalCode, addr.Country, addr.Phone);

        // Build at the EFFECTIVE (catalogue) price — never the client-submitted price.
        var items = request.Order.Items.Select(i =>
            OrderItem.Create(i.ProductId, i.ProductName, i.SKU, effectivePrices[i.ProductId], i.Quantity, i.ImageUrl)).ToList();

        // Order.Create validates invariants, generates the order number, sets status to Pending,
        // and raises an OrderCreatedEvent (domain event, dispatched after SaveChangesAsync).
        var order = OrderEntity.Create(request.UserId, request.CustomerEmail, request.CustomerName, shippingAddress, items, request.Order.Notes);
        await uow.Orders.AddAsync(order, ct);

        // Publish integration event to the message bus — this starts the SAGA in OrderSaga.
        // OrderItemPayload carries the product IDs and quantities needed by ReserveStockConsumer.
        var integrationEvent = new OrderCreatedIntegrationEvent(
            order.Id,
            order.UserId,
            order.CustomerEmail,
            order.CustomerName,
            order.OrderNumber,
            items.Select(i => new OrderItemPayload(i.ProductId, i.SKU, i.Quantity, i.Price)).ToList(),
            order.TotalAmount);

        await publisher.Publish(integrationEvent, ct);

        // SaveChangesAsync commits both the order row and the outbox message in one transaction.
        await uow.SaveChangesAsync(ct);
        order.ClearDomainEvents();

        // COMMIT-THEN-NOTIFY. The order + outbox are now durably committed. Only now do we emit the
        // customer notification as a FIRE-AND-FORGET Event Grid side-effect — strictly AFTER the
        // commit, never inside the business transaction. TryPublishAsync NEVER throws, so a
        // notification failure cannot fail this handler or roll back the created order. (A serverless
        // Function consumes the event and sends the email; that path is fully decoupled from here.)
        await sideEffects.TryPublishAsync(
            NotificationEventTypes.OrderCreated,
            $"orders/{order.Id}",
            new OrderCreatedNotification(
                order.CustomerEmail, order.CustomerName, order.OrderNumber,
                order.TotalAmount, OrderCurrency, DateTimeOffset.UtcNow),
            ct);

        return CreateOrderResult.Success(OrderMapper.ToDto(order));
    }
}
