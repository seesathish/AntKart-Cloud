using AK.Products.Domain.Entities;
using AK.Products.Application.Common;
using AK.Products.Application.Interfaces;
using AK.Products.Application.Queries.GetProducts;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Queries;

public sealed class GetProductsQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly Mock<IDiscountGrpcClient> _discountMock = new();
    private readonly GetProductsQueryHandler _handler;

    public GetProductsQueryHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _discountMock.Setup(d => d.GetDiscountAsync(It.IsAny<string>(), default))
            .ReturnsAsync((DiscountResult?)null);
        _handler = new GetProductsQueryHandler(_uowMock.Object, _discountMock.Object);
    }

    [Fact]
    public async Task Handle_WithNoFilters_ShouldReturnPagedResult()
    {
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateWomenProduct("SKU-002"),
            TestDataFactory.CreateKidsProduct("SKU-003")
        }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(Page: 1, PageSize: 20), default);

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithCategoryFilter_ShouldGetByCategory()
    {
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateMenProduct("SKU-002")
        }.AsReadOnly();
        _repoMock.Setup(r => r.GetByCategoryAsync("Men", default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(Category: "Men"), default);

        result.Items.Should().HaveCount(2);
        result.Items.All(p => p.CategoryName == "Men").Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSubCategoryFilter_ShouldFilterInMemory()
    {
        var shirts = TestDataFactory.CreateMenProduct("SKU-001");
        var dresses = TestDataFactory.CreateWomenProduct("SKU-002");
        var products = new List<Product> { shirts, dresses }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(SubCategory: "Shirts"), default);

        result.Items.Should().HaveCount(1);
        result.Items[0].SubCategoryName.Should().Be("Shirts");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ShouldFilterByName()
    {
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateWomenProduct("SKU-002")
        }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(SearchTerm: "Shirt"), default);

        result.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithPaging_ShouldReturnCorrectPage()
    {
        var products = Enumerable.Range(1, 25)
            .Select(i => TestDataFactory.CreateMenProduct($"SKU-{i:D3}"))
            .ToList().AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(Page: 2, PageSize: 10), default);

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithIsFeaturedFilter_ShouldFilterByFeatured()
    {
        var featured = TestDataFactory.CreateMenProduct("SKU-001");
        featured.SetFeatured(true);
        var notFeatured = TestDataFactory.CreateMenProduct("SKU-002");
        var products = new List<Product> { featured, notFeatured }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(IsFeatured: true), default);

        result.Items.Should().HaveCount(1);
        result.Items[0].IsFeatured.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ShouldFilterByBrand()
    {
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateWomenProduct("SKU-002")
        }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _handler.Handle(new GetProductsQuery(SearchTerm: "ArrowMen"), default);

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithActiveDiscounts_ShouldEnrichDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct("SKU-001");
        var products = new List<Product> { product }.AsReadOnly();
        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync(new DiscountResult(20.0, "Percentage", true));

        var result = await _handler.Handle(new GetProductsQuery(Page: 1, PageSize: 20), default);

        result.Items[0].DiscountPrice.Should().Be(ProductMapper.ComputeDiscountedPrice(product.Price, 20.0, "Percentage"));
    }
}
