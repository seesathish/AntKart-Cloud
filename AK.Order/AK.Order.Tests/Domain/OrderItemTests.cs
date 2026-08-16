using AK.Order.Domain.Entities;
using AK.Order.Tests.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;
using FluentAssertions;

namespace AK.Order.Tests.Domain;

public class OrderItemTests
{
    [Fact]
    public void Create_ValidInputs_ReturnsOrderItem()
    {
        var item = TestDataFactory.CreateOrderItem();
        item.ProductId.Should().Be("prod-001");
        item.ProductName.Should().Be("Test Product");
        item.SKU.Should().Be("MEN-SHIR-001");
        item.Price.Should().Be(29.99m);
        item.Quantity.Should().Be(2);
        item.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void Create_EmptyProductId_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create("", "Name", "SKU", 10m, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*ProductId*");
    }

    [Fact]
    public void Create_EmptyProductName_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create("id", "", "SKU", 10m, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*ProductName*");
    }

    [Fact]
    public void Create_EmptySKU_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create("id", "name", "", 10m, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*SKU*");
    }

    [Fact]
    public void Create_ZeroPrice_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create("id", "name", "sku", 0m, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*Price*");
    }

    [Fact]
    public void Create_ZeroQuantity_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create("id", "name", "sku", 10m, 0);
        act.Should().Throw<ArgumentException>().WithMessage("*Quantity*");
    }

    [Fact]
    public void SubTotal_ReturnsCorrectValue()
    {
        var item = TestDataFactory.CreateOrderItem(price: 15.00m, quantity: 3);
        item.SubTotal.Should().Be(45.00m);
    }

    [Fact]
    public void Create_WithImageUrl_SetsImageUrl()
    {
        var item = TestDataFactory.CreateOrderItem(imageUrl: "https://example.com/img.jpg");
        item.ImageUrl.Should().Be("https://example.com/img.jpg");
    }
}
