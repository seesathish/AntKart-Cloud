using AK.ShoppingCart.Domain.Events;

namespace AK.ShoppingCart.Domain.Entities;

// Cart aggregate root. A cart belongs to exactly one user and holds their in-progress items.
// The cart is stored in Redis (not a relational DB) because it's temporary, session-like data
// that needs fast read/write and a natural expiry (30-day TTL).
public sealed class Cart
{
    private readonly List<CartItem> _items = [];
    private readonly List<object> _domainEvents = [];

    // UserId is the caller's stable id — derived from the JWT 'sub' claim at the endpoint layer.
    public string UserId { get; private set; } = string.Empty;
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Computed on demand from the live items list.
    public decimal TotalAmount => _items.Sum(i => i.Price * i.Quantity);
    public int TotalItems => _items.Sum(i => i.Quantity);
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    private Cart() { }

    // Used when a user adds their first item — creates a brand new cart.
    public static Cart Create(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId cannot be empty", nameof(userId));

        return new Cart
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // Used by CartRepository when loading an existing cart from Redis.
    // Bypasses the Create factory so we don't reset CreatedAt to "now".
    // This is the snapshot restore pattern: deserialise the JSON snapshot → CartItem objects → Cart.
    public static Cart Restore(string userId, DateTime createdAt, DateTime updatedAt, IEnumerable<CartItem> items)
    {
        var cart = new Cart { UserId = userId, CreatedAt = createdAt, UpdatedAt = updatedAt };
        cart._items.AddRange(items);
        return cart;
    }

    // Adding the same product again increments quantity rather than creating a duplicate line.
    public void AddItem(string productId, string productName, string sku, decimal price, int quantity, string? imageUrl = null)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.UpdateQuantity(existing.Quantity + quantity);
        else
            _items.Add(CartItem.Create(productId, productName, sku, price, quantity, imageUrl));

        SetUpdated();
        _domainEvents.Add(new CartItemAddedEvent(UserId, productId, quantity));
    }

    public void RemoveItem(string productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Product '{productId}' not found in cart");

        _items.Remove(item);
        SetUpdated();
        _domainEvents.Add(new CartItemRemovedEvent(UserId, productId));
    }

    // Setting quantity to 0 (or negative) removes the item entirely — used by the update endpoint.
    public void UpdateItemQuantity(string productId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Product '{productId}' not found in cart");

        if (quantity <= 0)
            _items.Remove(item);
        else
            item.UpdateQuantity(quantity);

        SetUpdated();
    }

    // Called automatically when an order is confirmed (ClearCartOnOrderConfirmedConsumer).
    public void Clear()
    {
        _items.Clear();
        SetUpdated();
        _domainEvents.Add(new CartClearedEvent(UserId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void SetUpdated() => UpdatedAt = DateTime.UtcNow;
}
