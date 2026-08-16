using AK.Products.Application.DTOs;
using AK.Products.Application.Validators;
using FluentAssertions;

namespace AK.Products.Tests.Application.Validators;

public sealed class UpdateProductValidatorTests
{
    private readonly UpdateProductValidator _validator = new();

    [Fact]
    public void Validate_WithValidDto_ShouldPass()
    {
        var dto = new UpdateProductDto("Shirt", "A great shirt", "Brand", 599m, 10, "Cotton");
        _validator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldFail()
    {
        var dto = new UpdateProductDto("", "Description", "Brand", 599m, 10, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_WithEmptyDescription_ShouldFail()
    {
        var dto = new UpdateProductDto("Shirt", "", "Brand", 599m, 10, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    [Fact]
    public void Validate_WithEmptyBrand_ShouldFail()
    {
        var dto = new UpdateProductDto("Shirt", "Description", "", 599m, 10, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Brand");
    }

    [Fact]
    public void Validate_WithZeroPrice_ShouldFail()
    {
        var dto = new UpdateProductDto("Shirt", "Description", "Brand", 0m, 10, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Price");
    }

    [Fact]
    public void Validate_WithNegativePrice_ShouldFail()
    {
        var dto = new UpdateProductDto("Shirt", "Description", "Brand", -1m, 10, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Price");
    }

    [Fact]
    public void Validate_WithNegativeStock_ShouldFail()
    {
        var dto = new UpdateProductDto("Shirt", "Description", "Brand", 599m, -1, null);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "StockQuantity");
    }

    [Fact]
    public void Validate_WithZeroStock_ShouldPass()
    {
        var dto = new UpdateProductDto("Shirt", "Description", "Brand", 599m, 0, null);
        _validator.Validate(dto).IsValid.Should().BeTrue();
    }
}
