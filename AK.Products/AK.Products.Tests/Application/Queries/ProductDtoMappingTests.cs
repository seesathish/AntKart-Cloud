using AK.Products.Application.Commands.CreateProduct;
using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Queries;

public sealed class ProductDtoMappingTests
{
    [Fact]
    public async Task CreateProduct_ShouldMapAllDtoProperties()
    {
        var uowMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IProductRepository>();
        uowMock.Setup(u => u.Products).Returns(repoMock.Object);
        repoMock.Setup(r => r.SkuExistsAsync(It.IsAny<string>(), default)).ReturnsAsync(false);
        repoMock.Setup(r => r.AddAsync(It.IsAny<Product>(), default))
            .ReturnsAsync((Product p, CancellationToken _) => p);
        uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new CreateProductCommandHandler(uowMock.Object);
        var dto = TestDataFactory.CreateProductDto();
        var result = await handler.Handle(new CreateProductCommand(dto), default);

        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be(dto.Name);
        result.Description.Should().Be(dto.Description);
        result.SKU.Should().Be(dto.SKU);
        result.Brand.Should().Be(dto.Brand);
        result.CategoryName.Should().Be("Men");
        result.SubCategoryName.Should().Be("Shirts");
        result.Status.Should().NotBeEmpty();
        result.SubCategoryName.Should().Be(dto.SubCategoryName);
        result.Price.Should().Be(dto.Price);
        result.Currency.Should().Be(dto.Currency);
        result.DiscountPrice.Should().BeNull();
        result.StockQuantity.Should().Be(dto.StockQuantity);
        result.Sizes.Should().BeEquivalentTo(dto.Sizes);
        result.Colors.Should().BeEquivalentTo(dto.Colors);
        result.ImageUrls.Should().NotBeNull();
        result.Material.Should().Be(dto.Material);
        result.IsFeatured.Should().BeFalse();
        result.Rating.Should().Be(0);
        result.ReviewCount.Should().Be(0);
        result.Tags.Should().NotBeNull();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void ComputeDiscountedPrice_WithPercentageDiscount_ReturnsCorrectPrice()
    {
        var price = 100m;
        var result = ProductMapper.ComputeDiscountedPrice(price, 20.0, "Percentage");
        result.Should().Be(80m);
    }

    [Fact]
    public void ComputeDiscountedPrice_WithFixedDiscount_ReturnsCorrectPrice()
    {
        var price = 100m;
        var result = ProductMapper.ComputeDiscountedPrice(price, 15.0, "Fixed");
        result.Should().Be(85m);
    }

    [Fact]
    public void ComputeDiscountedPrice_WithCaseInsensitiveDiscountType_Works()
    {
        var price = 100m;
        var result = ProductMapper.ComputeDiscountedPrice(price, 10.0, "PERCENTAGE");
        result.Should().Be(90m);
    }

    [Fact]
    public void ToDto_WithDiscountedPriceOverride_UsesOverride()
    {
        var product = TestDataFactory.CreateMenProduct();
        var overridePrice = 49.99m;

        var dto = ProductMapper.ToDto(product, overridePrice);

        dto.DiscountPrice.Should().Be(overridePrice);
    }

    [Fact]
    public void ToDto_WithNoOverride_UsesEntityDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        var dto = ProductMapper.ToDto(product);

        dto.DiscountPrice.Should().Be(product.DiscountPrice);
    }
}
