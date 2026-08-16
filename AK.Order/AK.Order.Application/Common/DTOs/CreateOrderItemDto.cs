namespace AK.Order.Application.Common.DTOs;

public sealed record CreateOrderItemDto(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl = null);
