using AK.Discount.Application.Commands.CreateDiscount;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Validators;
using FluentAssertions;

namespace AK.Discount.Tests.Application.Validators;

public sealed class CreateDiscountValidatorTests
{
    private readonly CreateDiscountValidator _validator = new();

    private static CreateDiscountCommand ValidCommand() => new(new CreateCouponDto(
        "MEN-SHIR-001", "Test Shirt", "SAVE10", "10% off",
        10m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));

    [Fact]
    public void Validate_WithValidCommand_ShouldPass()
    {
        _validator.Validate(ValidCommand()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithFlatAmountType_ShouldPass()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "Test Shirt", "FLAT50", "50 off",
            50m, "FlatAmount", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyProductId_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "", "Test Shirt", "SAVE10", "10% off",
            10m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ProductId"));
    }

    [Fact]
    public void Validate_WithEmptyProductName_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "", "SAVE10", "10% off",
            10m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ProductName"));
    }

    [Fact]
    public void Validate_WithEmptyCouponCode_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "Test Shirt", "", "10% off",
            10m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("CouponCode"));
    }

    [Fact]
    public void Validate_WithZeroAmount_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "Test Shirt", "SAVE10", "10% off",
            0m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Amount"));
    }

    [Fact]
    public void Validate_WithInvalidDiscountType_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "Test Shirt", "SAVE10", "10% off",
            10m, "InvalidType", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("DiscountType"));
    }

    [Fact]
    public void Validate_WithValidToBeforeValidFrom_ShouldFail()
    {
        var cmd = new CreateDiscountCommand(new CreateCouponDto(
            "MEN-SHIR-001", "Test Shirt", "SAVE10", "10% off",
            10m, "Percentage", DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(1)));
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ValidTo"));
    }
}
