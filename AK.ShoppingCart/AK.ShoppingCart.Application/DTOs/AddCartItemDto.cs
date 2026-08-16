namespace AK.ShoppingCart.Application.DTOs;

public sealed record AddCartItemDto(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl = null);
