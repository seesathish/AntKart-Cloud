using AK.ShoppingCart.Application.Commands.AddToCart;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Validators;
using FluentAssertions;

namespace AK.ShoppingCart.Tests.Application.Validators;

public sealed class AddToCartValidatorTests
{
    private readonly AddToCartValidator _validator = new();

    private static AddToCartCommand ValidCommand() => new(
        "user-001",
        new AddCartItemDto("prod-001", "Shirt", "MEN-001", 999m, 2));

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        var result = _validator.Validate(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyUserId_ShouldFail(string userId)
    {
        var cmd = ValidCommand() with { UserId = userId };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_UserIdTooLong_ShouldFail()
    {
        var cmd = ValidCommand() with { UserId = new string('x', 101) };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyProductId_ShouldFail()
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("", "Shirt", "MEN-001", 999m, 2));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ProductId"));
    }

    [Fact]
    public void Validate_EmptyProductName_ShouldFail()
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", "", "MEN-001", 999m, 2));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptySku_ShouldFail()
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", "Shirt", "", 999m, 2));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidPrice_ShouldFail(decimal price)
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", "Shirt", "MEN-001", price, 2));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Price"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidQuantity_ShouldFail(int quantity)
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", "Shirt", "MEN-001", 999m, quantity));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Fact]
    public void Validate_ProductNameTooLong_ShouldFail()
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", new string('x', 201), "MEN-001", 999m, 1));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SkuTooLong_ShouldFail()
    {
        var cmd = new AddToCartCommand("user-001", new AddCartItemDto("prod-001", "Shirt", new string('x', 51), 999m, 1));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
