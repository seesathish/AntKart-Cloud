using AK.Products.Application.DTOs;
using AK.Products.Domain.Entities;

namespace AK.Products.Application.Common;

public static class ProductMapper
{
    public static ProductDto ToDto(Product p, decimal? discountedPrice = null) => new(
        p.Id, p.Name, p.Description, p.SKU, p.Brand,
        p.Status.ToString(),
        p.CategoryName, p.SubCategoryName,
        p.Price, p.Currency,
        discountedPrice ?? p.DiscountPrice,
        p.StockQuantity, p.Sizes, p.Colors, p.ImageUrls,
        p.Material, p.IsFeatured, p.Rating, p.ReviewCount,
        p.Tags, p.CreatedAt, p.UpdatedAt
    );

    public static IReadOnlyList<ProductDto> ToDtoList(IEnumerable<Product> products) =>
        products.Select(p => ToDto(p)).ToList().AsReadOnly();

    public static decimal? ComputeDiscountedPrice(decimal price, double amount, string discountType) =>
        discountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(price - price * (decimal)amount / 100, 2)
            : Math.Round(price - (decimal)amount, 2);
}
