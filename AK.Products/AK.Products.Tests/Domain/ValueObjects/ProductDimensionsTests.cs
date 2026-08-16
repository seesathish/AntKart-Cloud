using AK.Products.Domain.ValueObjects;
using FluentAssertions;

namespace AK.Products.Tests.Domain.ValueObjects;

public sealed class ProductDimensionsTests
{
    [Fact]
    public void Constructor_WithAllValues_ShouldSetProperties()
    {
        var dims = new ProductDimensions(1.5m, "kg", "L", "chart.pdf");
        dims.Weight.Should().Be(1.5m);
        dims.WeightUnit.Should().Be("kg");
        dims.Size.Should().Be("L");
        dims.SizeChart.Should().Be("chart.pdf");
    }

    [Fact]
    public void Constructor_WithDefaultWeightUnit_ShouldUseKg()
    {
        var dims = new ProductDimensions(2.0m);
        dims.WeightUnit.Should().Be("kg");
        dims.Size.Should().BeNull();
        dims.SizeChart.Should().BeNull();
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        var d1 = new ProductDimensions(1.5m, "kg", "L");
        var d2 = new ProductDimensions(1.5m, "kg", "L");
        d1.Should().Be(d2);
    }

    [Fact]
    public void Equality_WithDifferentWeight_ShouldNotBeEqual()
    {
        var d1 = new ProductDimensions(1.5m, "kg");
        var d2 = new ProductDimensions(2.0m, "kg");
        d1.Should().NotBe(d2);
    }

    [Fact]
    public void Equality_WithDifferentWeightUnit_ShouldNotBeEqual()
    {
        var d1 = new ProductDimensions(1.5m, "kg");
        var d2 = new ProductDimensions(1.5m, "lbs");
        d1.Should().NotBe(d2);
    }
}
