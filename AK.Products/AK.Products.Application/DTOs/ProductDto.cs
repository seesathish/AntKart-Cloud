namespace AK.Products.Application.DTOs;

public sealed record ProductDto(
    string Id,
    string Name,
    string Description,
    string SKU,
    string Brand,
    string Status,
    string CategoryName,
    string? SubCategoryName,
    decimal Price,
    string Currency,
    decimal? DiscountPrice,
    int StockQuantity,
    List<string> Sizes,
    List<string> Colors,
    List<string> ImageUrls,
    string? Material,
    bool IsFeatured,
    double Rating,
    int ReviewCount,
    List<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);
