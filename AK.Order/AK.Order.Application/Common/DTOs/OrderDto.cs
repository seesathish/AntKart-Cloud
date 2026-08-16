using AK.Order.Domain.Enums;

namespace AK.Order.Application.Common.DTOs;

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    string UserId,
    string Status,
    string PaymentStatus,
    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    decimal TotalAmount,
    int TotalItems,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
