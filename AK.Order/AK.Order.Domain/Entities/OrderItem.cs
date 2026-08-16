namespace AK.Order.Domain.Entities;

public sealed class OrderItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string SKU { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }
    public string? ImageUrl { get; private set; }
    public decimal SubTotal => Price * Quantity;

    private OrderItem() { }

    public static OrderItem Create(
        string productId, string productName, string sku,
        decimal price, int quantity, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("ProductId is required.", nameof(productId));
        if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("ProductName is required.", nameof(productName));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (price <= 0) throw new ArgumentException("Price must be greater than 0.", nameof(price));
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than 0.", nameof(quantity));

        return new OrderItem
        {
            ProductId = productId,
            ProductName = productName,
            SKU = sku,
            Price = price,
            Quantity = quantity,
            ImageUrl = imageUrl
        };
    }

    internal void IncrementQuantity(int additional)
    {
        if (additional <= 0) throw new ArgumentException("Additional quantity must be positive.", nameof(additional));
        Quantity += additional;
    }
}
