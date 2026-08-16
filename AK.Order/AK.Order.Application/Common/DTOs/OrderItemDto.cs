namespace AK.Order.Application.Common.DTOs;

public sealed record OrderItemDto(
    Guid Id,
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl,
    decimal SubTotal);
