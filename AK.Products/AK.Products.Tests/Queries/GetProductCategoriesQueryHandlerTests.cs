using AK.Products.Application.Interfaces;
using AK.Products.Application.Queries.GetProductCategories;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Queries;

public sealed class GetProductCategoriesQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IProductRepository> _products = new();

    public GetProductCategoriesQueryHandlerTests()
    {
        _uow.Setup(u => u.Products).Returns(_products.Object);
    }

    private GetProductCategoriesQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_ReturnsDistinctCategories()
    {
        var categories = new List<string> { "Men", "Women", "Kids" };
        _products.Setup(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        var result = await CreateHandler().Handle(new GetProductCategoriesQuery(), CancellationToken.None);

        result.Should().BeEquivalentTo(categories);
    }

    [Fact]
    public async Task Handle_WhenNoProducts_ReturnsEmptyList()
    {
        _products.Setup(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var result = await CreateHandler().Handle(new GetProductCategoriesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DelegatesToProductRepository()
    {
        _products.Setup(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        await CreateHandler().Handle(new GetProductCategoriesQuery(), CancellationToken.None);

        _products.Verify(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        _products.Setup(r => r.GetDistinctCategoriesAsync(cts.Token))
            .ReturnsAsync(new List<string> { "Men" });

        await CreateHandler().Handle(new GetProductCategoriesQuery(), cts.Token);

        _products.Verify(r => r.GetDistinctCategoriesAsync(cts.Token), Times.Once);
    }
}
