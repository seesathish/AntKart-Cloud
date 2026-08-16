using AK.BuildingBlocks.DDD;
using AK.Order.Domain.Enums;
using AK.Order.Domain.Events;
using AK.Order.Domain.ValueObjects;

namespace AK.Order.Domain.Entities;

// Order is the Aggregate Root for the order bounded context.
// All changes to an order must go through this class — never modify child collections directly.
// Properties have private setters to enforce that all business logic stays inside this class.
public sealed class Order : Entity, IAggregateRoot
{
    // Private backing field for items — callers can only read via the Items property.
    private readonly List<OrderItem> _items = [];

    public string OrderNumber { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public string? Notes { get; private set; }

    // Computed properties — recalculated on every access from the live items list.
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    public decimal TotalAmount => _items.Sum(i => i.Price * i.Quantity);
    public int TotalItems => _items.Sum(i => i.Quantity);

    // Private constructor forces all creation through the factory method below.
    // EF Core also needs a parameterless constructor to materialise entities from the database.
    private Order() { }

    // Factory method: the only valid way to create a new Order.
    // Validates all invariants, generates the order number, sets initial state, and raises
    // an OrderCreatedEvent so the SAGA and notification service know an order was placed.
    public static Order Create(
        string userId,
        string customerEmail,
        string customerName,
        ShippingAddress shippingAddress,
        IEnumerable<OrderItem> items,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        ArgumentNullException.ThrowIfNull(shippingAddress);

        var itemList = items.ToList();
        if (itemList.Count == 0)
            throw new ArgumentException("Order must contain at least one item.", nameof(items));

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(),
            UserId = userId.Trim(),
            CustomerEmail = customerEmail.Trim(),
            CustomerName = customerName.Trim(),
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Notes = notes?.Trim()
        };

        order._items.AddRange(itemList);

        // Raise domain event — the SAGA (OrderSaga) will pick this up via the message bus
        // to start the stock reservation process.
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, order.UserId, order.OrderNumber));
        return order;
    }

    // State machine: defines which status transitions are legal.
    // Attempting an invalid transition (e.g. Delivered → Pending) throws InvalidOperationException,
    // which the ExceptionHandlerMiddleware maps to HTTP 409 Conflict.
    // Empty HashSets for Delivered and Cancelled mark them as terminal (no further transitions).
    //
    // Payment outcomes: Confirmed is the state an order occupies when payment is attempted
    // (OrderConfirmedConsumer sets Confirmed after the saga reserves stock), so BOTH payment
    // outcomes — Paid and PaymentFailed — must be reachable from Confirmed. Paid moves FORWARD
    // into fulfilment, never back to Confirmed. PaymentFailed can reach Paid so a customer retry
    // can succeed.
    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> _allowedTransitions = new()
    {
        [OrderStatus.Pending]       = [OrderStatus.Confirmed, OrderStatus.Cancelled, OrderStatus.PaymentFailed],
        [OrderStatus.Confirmed]     = [OrderStatus.Paid, OrderStatus.PaymentFailed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Processing]    = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped]       = [OrderStatus.Delivered],
        [OrderStatus.Delivered]     = [],   // terminal — no further transitions allowed
        [OrderStatus.Cancelled]     = [],   // terminal
        [OrderStatus.Paid]          = [OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Paid, OrderStatus.Cancelled],
    };

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (newStatus == Status) return;

        if (!_allowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException(
                $"Cannot transition order from {Status} to {newStatus}.");

        var oldStatus = Status;
        Status = newStatus;
        SetUpdatedAt();
        AddDomainEvent(new OrderStatusChangedEvent(Id, oldStatus, newStatus));
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");
        if (Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Cannot cancel a delivered order.");

        Status = OrderStatus.Cancelled;
        SetUpdatedAt();

        // Raise domain event — OrderCancelledConsumer in AK.Order and AK.Notification
        // will update the order status and send a cancellation email respectively.
        AddDomainEvent(new OrderCancelledEvent(Id, UserId, CustomerEmail, CustomerName, OrderNumber));
    }

    // Called by PaymentSucceededConsumer after Razorpay confirms payment.
    public void ConfirmPayment()
    {
        PaymentStatus = PaymentStatus.Paid;
        SetUpdatedAt();
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // If the same product is added again, increment quantity instead of adding a duplicate line.
        var existing = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existing is not null)
            existing.IncrementQuantity(item.Quantity);
        else
            _items.Add(item);

        SetUpdatedAt();
    }

    // Format: ORD-20260418-A1B2C3D4 — date + 8 uppercase hex chars from a new GUID.
    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
}
