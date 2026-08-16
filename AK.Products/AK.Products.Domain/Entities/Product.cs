using AK.BuildingBlocks.DDD;
using AK.Products.Domain.Enums;
using AK.Products.Domain.Events;
using AK.Products.Domain.ValueObjects;

namespace AK.Products.Domain.Entities;

// Product is the Aggregate Root for the products bounded context.
// All properties use private setters — you can never set product.Price = 99 from outside.
// All mutations must go through the public methods (Update, SetDiscount, DecrementStock, etc.)
// which enforce business rules and raise domain events.
//
// Category design: CategoryName and SubCategoryName are plain strings (not enums).
// Adding a new category (e.g. "Electronics") is purely a data change — no code deployment needed.
public sealed class Product : StringEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    // SKU format: {CAT_ABBREV}-{SUBCAT_ABBREV}-{001..NNN}  e.g. MEN-SHIR-001
    public string SKU { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public ProductStatus Status { get; private set; }

    // Data-driven categories — no enum. Allows adding new categories without code changes.
    public string CategoryName { get; private set; } = string.Empty;
    public string? SubCategoryName { get; private set; }

    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";

    // DiscountPrice is computed by AK.Discount (via gRPC) and set here for query responses.
    // Null means no active discount exists for this product.
    public decimal? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public List<string> Sizes { get; private set; } = new();
    public List<string> Colors { get; private set; } = new();
    public List<string> ImageUrls { get; private set; } = new();
    public string? Material { get; private set; }
    public bool IsFeatured { get; private set; }
    public double Rating { get; private set; }
    public int ReviewCount { get; private set; }
    public List<string> Tags { get; private set; } = new();

    // Private constructor: forces all creation through the factory method.
    // MongoDB and EF Core also require a parameterless constructor for deserialisation.
    private Product() { }

    // Factory method: the only valid way to create a new Product.
    // Validates required fields, auto-derives the initial status from stock,
    // and raises ProductCreatedEvent.
    public static Product Create(
        string name, string description, string sku, string brand,
        string categoryName, string? subCategoryName,
        decimal price, string currency, int stockQuantity,
        List<string> sizes, List<string> colors, string? material = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(categoryName)) throw new ArgumentException("CategoryName is required.", nameof(categoryName));

        var product = new Product
        {
            Name = name,
            Description = description,
            SKU = sku,
            Brand = brand,
            CategoryName = categoryName,
            SubCategoryName = subCategoryName,
            Price = price,
            Currency = currency,
            StockQuantity = stockQuantity,
            // Automatically set to OutOfStock if no inventory is supplied.
            Status = stockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock,
            Sizes = sizes,
            Colors = colors,
            Material = material
        };
        product.AddDomainEvent(new ProductCreatedEvent(product.Id, product.Name));
        return product;
    }

    // Reconstitutes a product with a caller-supplied DETERMINISTIC id (used by the seed loader).
    // The loader derives the id from the stable SKU, so the same SKU always maps to the same
    // document — making the load an idempotent, single-partition point upsert on the `_id` hashed
    // shard key (re-running never creates duplicates). Domain events are cleared because a seed
    // import is not a domain "create".
    public static Product CreateForSeed(
        string id, string name, string description, string sku, string brand,
        string categoryName, string? subCategoryName,
        decimal price, string currency, int stockQuantity,
        List<string> sizes, List<string> colors, string? material = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));

        var product = Create(name, description, sku, brand, categoryName, subCategoryName,
            price, currency, stockQuantity, sizes, colors, material);
        product.Id = id;
        product.ClearDomainEvents();
        return product;
    }

    public void Update(string name, string description, string brand, decimal price, int stockQuantity, string? material)
    {
        Name = name;
        Description = description;
        Brand = brand;
        Price = price;
        StockQuantity = stockQuantity;
        Material = material;
        Status = stockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock;
        SetUpdatedAt();
        AddDomainEvent(new ProductUpdatedEvent(Id, Name));
    }

    public void SetDiscount(decimal discountPrice)
    {
        if (discountPrice >= Price) throw new InvalidOperationException("Discount price must be less than original price");
        DiscountPrice = discountPrice;
        SetUpdatedAt();
    }

    public void RemoveDiscount() { DiscountPrice = null; SetUpdatedAt(); }

    public void SetFeatured(bool isFeatured) { IsFeatured = isFeatured; SetUpdatedAt(); }

    // Incremental running average — avoids storing all individual ratings.
    // New average = (old_avg × old_count + new_rating) / (old_count + 1)
    public void AddReview(double rating)
    {
        Rating = ((Rating * ReviewCount) + rating) / (ReviewCount + 1);
        ReviewCount++;
        SetUpdatedAt();
    }

    // Called by ReserveStockConsumer when an order is placed.
    // Throws if stock is insufficient — the caller then publishes StockReservationFailed.
    // Automatically transitions to OutOfStock status if stock reaches zero.
    public void DecrementStock(int quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (StockQuantity < quantity)
            throw new InvalidOperationException(
                $"Insufficient stock for SKU '{SKU}'. Available: {StockQuantity}, Requested: {quantity}");
        StockQuantity -= quantity;
        Status = StockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock;
        SetUpdatedAt();
    }

    public void Deactivate() { Status = ProductStatus.Inactive; SetUpdatedAt(); }

    public Money GetPrice() => new(Price, Currency);
    public Money? GetDiscountPrice() => DiscountPrice.HasValue ? new(DiscountPrice.Value, Currency) : null;
}
