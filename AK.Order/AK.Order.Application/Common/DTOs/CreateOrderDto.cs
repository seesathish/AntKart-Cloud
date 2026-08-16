namespace AK.Order.Application.Common.DTOs;

public sealed record CreateOrderDto(
    ShippingAddressDto ShippingAddress,
    List<CreateOrderItemDto> Items,
    string? Notes = null);
