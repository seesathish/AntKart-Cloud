using AK.Products.Application.DTOs;
using AK.Products.Application.Validators;
using FluentAssertions;

namespace AK.Products.Tests.Application.Validators;

public sealed class CreateProductValidatorTests
{
    private readonly CreateProductValidator _validator = new();

    [Fact]
    public void Validate_WithValidDto_ShouldPass()
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            "Men", "Shirts", 599m, "USD", 10, ["M"], ["White"], null);

        _validator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldFail()
    {
        var dto = new CreateProductDto("", "Description", "SKU-001", "Brand",
            "Men", "Shirts", 599m, "USD", 10, ["M"], ["White"], null);

        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_WithNegativePrice_ShouldFail()
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            "Men", "Shirts", -100m, "USD", 10, ["M"], ["White"], null);

        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Price");
    }

    [Fact]
    public void Validate_WithEmptySizes_ShouldFail()
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            "Men", "Shirts", 599m, "USD", 10, [], ["White"], null);

        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Sizes");
    }

    [Fact]
    public void Validate_WithNegativeStock_ShouldFail()
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            "Men", "Shirts", 599m, "USD", -1, ["M"], ["White"], null);

        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "StockQuantity");
    }

    [Fact]
    public void Validate_WithEmptyCategoryName_ShouldFail()
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            "", null, 599m, "USD", 10, ["M"], ["White"], null);

        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CategoryName");
    }

    [Theory]
    [InlineData("Men")]
    [InlineData("Women")]
    [InlineData("Kids")]
    [InlineData("Sports")]
    [InlineData("Formal")]
    public void Validate_WithAnyCategoryName_ShouldPass(string categoryName)
    {
        var dto = new CreateProductDto("Shirt", "Description", "SKU-001", "Brand",
            categoryName, null, 599m, "USD", 10, ["M"], ["White"], null);

        _validator.Validate(dto).IsValid.Should().BeTrue();
    }
}
