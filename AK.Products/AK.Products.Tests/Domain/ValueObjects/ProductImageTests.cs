using AK.Products.Domain.ValueObjects;
using FluentAssertions;

namespace AK.Products.Tests.Domain.ValueObjects;

public sealed class ProductImageTests
{
    [Fact]
    public void Constructor_WithAllValues_ShouldSetProperties()
    {
        var image = new ProductImage("https://example.com/img.jpg", "Product image", true);
        image.Url.Should().Be("https://example.com/img.jpg");
        image.AltText.Should().Be("Product image");
        image.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void Constructor_DefaultIsPrimary_ShouldBeFalse()
    {
        var image = new ProductImage("https://example.com/img.jpg", "Alt text");
        image.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        var i1 = new ProductImage("https://example.com/img.jpg", "Alt", true);
        var i2 = new ProductImage("https://example.com/img.jpg", "Alt", true);
        i1.Should().Be(i2);
    }

    [Fact]
    public void Equality_WithDifferentUrl_ShouldNotBeEqual()
    {
        var i1 = new ProductImage("https://example.com/img1.jpg", "Alt");
        var i2 = new ProductImage("https://example.com/img2.jpg", "Alt");
        i1.Should().NotBe(i2);
    }

    [Fact]
    public void Equality_WithDifferentIsPrimary_ShouldNotBeEqual()
    {
        var i1 = new ProductImage("https://example.com/img.jpg", "Alt", true);
        var i2 = new ProductImage("https://example.com/img.jpg", "Alt", false);
        i1.Should().NotBe(i2);
    }
}
