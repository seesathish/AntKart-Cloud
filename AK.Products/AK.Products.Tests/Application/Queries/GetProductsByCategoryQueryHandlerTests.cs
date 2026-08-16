using AK.Products.Application.Common;
using AK.Products.Application.Interfaces;
using AK.Products.Application.Queries.GetProductsByCategory;
using AK.Products.Domain.Entities;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Queries;

public sealed class GetProductsByCategoryQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly Mock<IDiscountGrpcClient> _discountMock = new();
    private readonly GetProductsByCategoryQueryHandler _handler;

    public GetProductsByCategoryQueryHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _discountMock.Setup(d => d.GetDiscountAsync(It.IsAny<string>(), default))
            .ReturnsAsync((DiscountResult?)null);
        _handler = new GetProductsByCategoryQueryHandler(_uowMock.Object, _discountMock.Object);
    }

    [Fact]
    public async Task Handle_WithMatchingProducts_ShouldReturnDtoList()
    {
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateMenProduct("SKU-002")
        }.AsReadOnly();
        _repoMock.Setup(r => r.GetByCategoryAsync("Men", default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsByCategoryQuery("Men"), default);

        result.Should().HaveCount(2);
        result.All(p => p.CategoryName == "Men").Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNoMatchingProducts_ShouldReturnEmptyList()
    {
        _repoMock.Setup(r => r.GetByCategoryAsync("NonExistent", default))
            .ReturnsAsync(new List<Product>().AsReadOnly());

        var result = await _handler.Handle(new GetProductsByCategoryQuery("NonExistent"), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldMapProductsToDtos()
    {
        var product = TestDataFactory.CreateMenProduct("MEN-SHRT-001");
        _repoMock.Setup(r => r.GetByCategoryAsync("Men", default))
            .ReturnsAsync(new List<Product> { product }.AsReadOnly());

        var result = await _handler.Handle(new GetProductsByCategoryQuery("Men"), default);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be(product.Name);
        result[0].SKU.Should().Be(product.SKU);
        result[0].CategoryName.Should().Be("Men");
        result[0].SubCategoryName.Should().Be("Shirts");
    }

    [Fact]
    public async Task Handle_WithActiveDiscount_ShouldEnrichDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct("MEN-SHRT-001");
        _repoMock.Setup(r => r.GetByCategoryAsync("Men", default))
            .ReturnsAsync(new List<Product> { product }.AsReadOnly());
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync(new DiscountResult(15.0, "Percentage", true));

        var result = await _handler.Handle(new GetProductsByCategoryQuery("Men"), default);

        result[0].DiscountPrice.Should().Be(ProductMapper.ComputeDiscountedPrice(product.Price, 15.0, "Percentage"));
    }

    [Theory]
    [InlineData("Men")]
    [InlineData("Women")]
    [InlineData("Kids")]
    [InlineData("Sports")]
    public async Task Handle_WithAnyCategory_ShouldCallRepositoryWithThatCategory(string category)
    {
        _repoMock.Setup(r => r.GetByCategoryAsync(category, default))
            .ReturnsAsync(new List<Product>().AsReadOnly());

        await _handler.Handle(new GetProductsByCategoryQuery(category), default);

        _repoMock.Verify(r => r.GetByCategoryAsync(category, default), Times.Once);
    }
}
