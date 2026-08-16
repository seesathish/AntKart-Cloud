using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Domain.Events;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;

namespace AK.ShoppingCart.Tests.Domain;

public sealed class CartTests
{
    [Fact]
    public void Create_WithValidUserId_ShouldCreateEmptyCart()
    {
        var cart = Cart.Create("user-001");

        cart.UserId.Should().Be("user-001");
        cart.Items.Should().BeEmpty();
        cart.TotalAmount.Should().Be(0);
        cart.TotalItems.Should().Be(0);
    }

    [Fact]
    public void Create_WithEmptyUserId_ShouldThrow()
    {
        var act = () => Cart.Create(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithWhitespaceUserId_ShouldThrow()
    {
        var act = () => Cart.Create("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddItem_NewProduct_ShouldAddToItems()
    {
        var cart = TestDataFactory.CreateEmptyCart();
        cart.AddItem("prod-001", "Shirt", "MEN-001", 500m, 2);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].ProductId.Should().Be("prod-001");
        cart.Items[0].Quantity.Should().Be(2);
    }

    [Fact]
    public void AddItem_ExistingProduct_ShouldIncrementQuantity()
    {
        var cart = TestDataFactory.CreateEmptyCart();
        cart.AddItem("prod-001", "Shirt", "MEN-001", 500m, 2);
        cart.AddItem("prod-001", "Shirt", "MEN-001", 500m, 3);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    public void AddItem_ShouldRaiseCartItemAddedEvent()
    {
        var cart = TestDataFactory.CreateEmptyCart();
        cart.AddItem("prod-001", "Shirt", "MEN-001", 500m, 2);

        cart.DomainEvents.Should().ContainSingle(e => e is CartItemAddedEvent);
        var ev = cart.DomainEvents.OfType<CartItemAddedEvent>().First();
        ev.ProductId.Should().Be("prod-001");
        ev.Quantity.Should().Be(2);
    }

    [Fact]
    public void RemoveItem_ExistingProduct_ShouldRemoveFromItems()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.RemoveItem(TestDataFactory.DefaultProductId);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_NonExistentProduct_ShouldThrow()
    {
        var cart = TestDataFactory.CreateEmptyCart();
        var act = () => cart.RemoveItem("nonexistent");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void RemoveItem_ShouldRaiseCartItemRemovedEvent()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.ClearDomainEvents();
        cart.RemoveItem(TestDataFactory.DefaultProductId);

        cart.DomainEvents.Should().ContainSingle(e => e is CartItemRemovedEvent);
    }

    [Fact]
    public void UpdateItemQuantity_ValidQuantity_ShouldUpdateItem()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.UpdateItemQuantity(TestDataFactory.DefaultProductId, 5);

        cart.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    public void UpdateItemQuantity_ZeroQuantity_ShouldRemoveItem()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.UpdateItemQuantity(TestDataFactory.DefaultProductId, 0);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void UpdateItemQuantity_NegativeQuantity_ShouldRemoveItem()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.UpdateItemQuantity(TestDataFactory.DefaultProductId, -1);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void UpdateItemQuantity_NonExistentProduct_ShouldThrow()
    {
        var cart = TestDataFactory.CreateEmptyCart();
        var act = () => cart.UpdateItemQuantity("nonexistent", 1);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Clear_ShouldRemoveAllItems()
    {
        var cart = TestDataFactory.CreateCartWithMultipleItems();
        cart.Clear();

        cart.Items.Should().BeEmpty();
        cart.TotalAmount.Should().Be(0);
        cart.TotalItems.Should().Be(0);
    }

    [Fact]
    public void Clear_ShouldRaiseCartClearedEvent()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.ClearDomainEvents();
        cart.Clear();

        cart.DomainEvents.Should().ContainSingle(e => e is CartClearedEvent);
    }

    [Fact]
    public void TotalAmount_ShouldSumAllItemSubTotals()
    {
        var cart = TestDataFactory.CreateCartWithMultipleItems();

        var expected = 999.99m * 2 + 1499.99m * 1 + 399.99m * 3;
        cart.TotalAmount.Should().Be(expected);
    }

    [Fact]
    public void TotalItems_ShouldSumAllQuantities()
    {
        var cart = TestDataFactory.CreateCartWithMultipleItems();
        cart.TotalItems.Should().Be(6); // 2 + 1 + 3
    }

    [Fact]
    public void Restore_ShouldReconstructCartFromData()
    {
        var items = new[] { CartItem.Restore("p1", "Shirt", "MEN-001", 100m, 2, null) };
        var now = DateTime.UtcNow;
        var cart = Cart.Restore("user-1", now, now, items);

        cart.UserId.Should().Be("user-1");
        cart.Items.Should().HaveCount(1);
        cart.Items[0].ProductId.Should().Be("p1");
    }

    [Fact]
    public void ClearDomainEvents_ShouldEmptyEventList()
    {
        var cart = TestDataFactory.CreateCartWithItem();
        cart.DomainEvents.Should().NotBeEmpty();

        cart.ClearDomainEvents();
        cart.DomainEvents.Should().BeEmpty();
    }
}
