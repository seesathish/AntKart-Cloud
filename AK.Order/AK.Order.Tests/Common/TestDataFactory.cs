using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Entities;
using AK.Order.Domain.Enums;
using AK.Order.Domain.ValueObjects;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Tests.Common;

public static class TestDataFactory
{
    public static ShippingAddress CreateShippingAddress(
        string fullName = "John Doe",
        string addressLine1 = "123 Main St",
        string? addressLine2 = null,
        string city = "Springfield",
        string state = "IL",
        string postalCode = "62701",
        string country = "US",
        string phone = "+1-555-0100") =>
        ShippingAddress.Create(fullName, addressLine1, addressLine2, city, state, postalCode, country, phone);

    public static OrderItem CreateOrderItem(
        string productId = "prod-001",
        string productName = "Test Product",
        string sku = "MEN-SHIR-001",
        decimal price = 29.99m,
        int quantity = 2,
        string? imageUrl = null) =>
        OrderItem.Create(productId, productName, sku, price, quantity, imageUrl);

    public static OrderEntity CreateOrder(
        string userId = "user-123",
        string customerEmail = "john@example.com",
        string customerName = "John Doe",
        ShippingAddress? shippingAddress = null,
        List<OrderItem>? items = null,
        string? notes = null)
    {
        shippingAddress ??= CreateShippingAddress();
        items ??= [CreateOrderItem()];
        return OrderEntity.Create(userId, customerEmail, customerName, shippingAddress, items, notes);
    }

    public static CreateOrderDto CreateOrderDto() => new(
        new ShippingAddressDto("John Doe", "123 Main St", null, "Springfield", "IL", "62701", "US", "+1-555-0100"),
        [new CreateOrderItemDto("prod-001", "Test Product", "MEN-SHIR-001", 29.99m, 2, null)],
        "Test notes");
}
