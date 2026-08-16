namespace AK.Products.Application.DTOs;

public sealed record UpdateProductDto(
    string Name,
    string Description,
    string Brand,
    decimal Price,
    int StockQuantity,
    string? Material
);
