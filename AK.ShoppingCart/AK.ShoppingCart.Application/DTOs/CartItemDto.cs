namespace AK.ShoppingCart.Application.DTOs;

public sealed record CartItemDto(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl,
    decimal SubTotal);
