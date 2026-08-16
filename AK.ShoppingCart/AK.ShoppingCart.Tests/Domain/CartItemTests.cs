using AK.ShoppingCart.Domain.Entities;
using FluentAssertions;

namespace AK.ShoppingCart.Tests.Domain;

public sealed class CartItemTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateCartItem()
    {
        var item = CartItem.Create("prod-001", "Shirt", "MEN-001", 999m, 2, "https://img.com/shirt.jpg");

        item.ProductId.Should().Be("prod-001");
        item.ProductName.Should().Be("Shirt");
        item.SKU.Should().Be("MEN-001");
        item.Price.Should().Be(999m);
        item.Quantity.Should().Be(2);
        item.ImageUrl.Should().Be("https://img.com/shirt.jpg");
    }

    [Fact]
    public void Create_WithoutImageUrl_ShouldCreateWithNullImageUrl()
    {
        var item = CartItem.Create("prod-001", "Shirt", "MEN-001", 999m, 2);
        item.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyProductId_ShouldThrow()
    {
        var act = () => CartItem.Create(string.Empty, "Shirt", "MEN-001", 999m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyProductName_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", string.Empty, "MEN-001", 999m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptySku_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", "Shirt", string.Empty, 999m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithZeroPrice_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", "Shirt", "MEN-001", 0m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithNegativePrice_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", "Shirt", "MEN-001", -1m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithZeroQuantity_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", "Shirt", "MEN-001", 999m, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithNegativeQuantity_ShouldThrow()
    {
        var act = () => CartItem.Create("prod-001", "Shirt", "MEN-001", 999m, -1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Restore_ShouldReconstructItemFromData()
    {
        var item = CartItem.Restore("prod-001", "Shirt", "MEN-001", 999m, 3, "https://img.com/shirt.jpg");

        item.ProductId.Should().Be("prod-001");
        item.ProductName.Should().Be("Shirt");
        item.SKU.Should().Be("MEN-001");
        item.Price.Should().Be(999m);
        item.Quantity.Should().Be(3);
        item.ImageUrl.Should().Be("https://img.com/shirt.jpg");
    }
}
