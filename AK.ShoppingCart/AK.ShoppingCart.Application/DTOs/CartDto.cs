namespace AK.ShoppingCart.Application.DTOs;

public sealed record CartDto(
    string UserId,
    IReadOnlyList<CartItemDto> Items,
    decimal TotalAmount,
    int TotalItems,
    DateTime CreatedAt,
    DateTime UpdatedAt);
