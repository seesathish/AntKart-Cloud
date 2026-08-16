using AK.Products.Domain.Entities;
using AK.Products.Application.Common;
using AK.Products.Application.Interfaces;
using AK.Products.Application.Queries.GetProductById;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Queries;

public sealed class GetProductByIdQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly Mock<IDiscountGrpcClient> _discountMock = new();
    private readonly GetProductByIdQueryHandler _handler;

    public GetProductByIdQueryHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _discountMock.Setup(d => d.GetDiscountAsync(It.IsAny<string>(), default))
            .ReturnsAsync((DiscountResult?)null);
        _handler = new GetProductByIdQueryHandler(_uowMock.Object, _discountMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldReturnProductDto()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Should().NotBeNull();
        result!.Name.Should().Be(product.Name);
        result.SKU.Should().Be(product.SKU);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNull()
    {
        _repoMock.Setup(r => r.GetByIdAsync("nonexistent", default))
            .ReturnsAsync((Product?)null);

        var result = await _handler.Handle(new GetProductByIdQuery("nonexistent"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithActivePercentageDiscount_ShouldReturnDiscountedPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync(new DiscountResult(10.0, "Percentage", true));

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Should().NotBeNull();
        result!.DiscountPrice.Should().Be(ProductMapper.ComputeDiscountedPrice(product.Price, 10.0, "Percentage"));
    }

    [Fact]
    public async Task Handle_WithActiveFixedDiscount_ShouldReturnDiscountedPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync(new DiscountResult(5.0, "Fixed", true));

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Should().NotBeNull();
        result!.DiscountPrice.Should().Be(ProductMapper.ComputeDiscountedPrice(product.Price, 5.0, "Fixed"));
    }

    [Fact]
    public async Task Handle_WithInactiveDiscount_ShouldReturnNullDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync(new DiscountResult(10.0, "Percentage", false));

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Should().NotBeNull();
        result!.DiscountPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenDiscountServiceUnavailable_ShouldReturnProductWithoutDiscount()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);
        _discountMock.Setup(d => d.GetDiscountAsync(product.Id, default))
            .ReturnsAsync((DiscountResult?)null);

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Should().NotBeNull();
        result!.DiscountPrice.Should().BeNull();
    }
}
