using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Entities;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Application.Common.Mapping;

internal static class OrderMapper
{
    internal static OrderDto ToDto(OrderEntity order) => new(
        order.Id,
        order.OrderNumber,
        order.UserId,
        order.Status.ToString(),
        order.PaymentStatus.ToString(),
        new ShippingAddressDto(
            order.ShippingAddress.FullName,
            order.ShippingAddress.AddressLine1,
            order.ShippingAddress.AddressLine2,
            order.ShippingAddress.City,
            order.ShippingAddress.State,
            order.ShippingAddress.PostalCode,
            order.ShippingAddress.Country,
            order.ShippingAddress.Phone),
        order.Items.Select(ToItemDto).ToList().AsReadOnly(),
        order.TotalAmount,
        order.TotalItems,
        order.Notes,
        order.CreatedAt,
        order.UpdatedAt);

    private static OrderItemDto ToItemDto(OrderItem item) => new(
        item.Id,
        item.ProductId,
        item.ProductName,
        item.SKU,
        item.Price,
        item.Quantity,
        item.ImageUrl,
        item.SubTotal);
}
