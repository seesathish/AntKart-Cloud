using AK.ShoppingCart.Application.Commands.UpdateCartItem;
using AK.ShoppingCart.Application.Validators;
using FluentAssertions;

namespace AK.ShoppingCart.Tests.Application.Validators;

public sealed class UpdateCartItemValidatorTests
{
    private readonly UpdateCartItemValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        var result = _validator.Validate(new UpdateCartItemCommand("user-001", "prod-001", 3));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroQuantity_ShouldPass()
    {
        var result = _validator.Validate(new UpdateCartItemCommand("user-001", "prod-001", 0));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyUserId_ShouldFail(string userId)
    {
        var result = _validator.Validate(new UpdateCartItemCommand(userId, "prod-001", 1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyProductId_ShouldFail(string productId)
    {
        var result = _validator.Validate(new UpdateCartItemCommand("user-001", productId, 1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ProductId");
    }

    [Fact]
    public void Validate_NegativeQuantity_ShouldFail()
    {
        var result = _validator.Validate(new UpdateCartItemCommand("user-001", "prod-001", -1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Quantity");
    }
}
