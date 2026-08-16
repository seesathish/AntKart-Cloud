namespace AK.ShoppingCart.Domain.Entities;

public sealed class CartItem
{
    public string ProductId { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string SKU { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }
    public string? ImageUrl { get; private set; }

    private CartItem() { }

    public static CartItem Create(string productId, string productName, string sku, decimal price, int quantity, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("ProductId is required", nameof(productId));
        if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("ProductName is required", nameof(productName));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required", nameof(sku));
        if (price <= 0) throw new ArgumentException("Price must be greater than 0", nameof(price));
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than 0", nameof(quantity));

        return new CartItem
        {
            ProductId = productId,
            ProductName = productName,
            SKU = sku,
            Price = price,
            Quantity = quantity,
            ImageUrl = imageUrl
        };
    }

    public static CartItem Restore(string productId, string productName, string sku, decimal price, int quantity, string? imageUrl)
        => new() { ProductId = productId, ProductName = productName, SKU = sku, Price = price, Quantity = quantity, ImageUrl = imageUrl };

    internal void UpdateQuantity(int quantity) => Quantity = quantity;
}
