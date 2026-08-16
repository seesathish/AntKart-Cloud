using AK.Products.Application.Commands.BulkUpdateProducts;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Commands;

public sealed class BulkUpdateProductsCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly BulkUpdateProductsCommandHandler _handler;

    public BulkUpdateProductsCommandHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _handler = new BulkUpdateProductsCommandHandler(_uowMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingProducts_ShouldUpdateAllAndReturnCount()
    {
        var p1 = TestDataFactory.CreateMenProduct("SKU-001");
        var p2 = TestDataFactory.CreateWomenProduct("SKU-002");
        var updates = new List<BulkUpdateProductDto>
        {
            new(p1.Id, TestDataFactory.UpdateProductDto()),
            new(p2.Id, TestDataFactory.UpdateProductDto())
        };
        _repoMock.Setup(r => r.GetByIdAsync(p1.Id, default)).ReturnsAsync(p1);
        _repoMock.Setup(r => r.GetByIdAsync(p2.Id, default)).ReturnsAsync(p2);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new BulkUpdateProductsCommand(updates), default);

        result.Should().Be(2);
        _repoMock.Verify(r => r.BulkUpdateAsync(It.IsAny<IEnumerable<Product>>(), default), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldSkipItAndReturnLowerCount()
    {
        var p1 = TestDataFactory.CreateMenProduct("SKU-001");
        var updates = new List<BulkUpdateProductDto>
        {
            new(p1.Id, TestDataFactory.UpdateProductDto()),
            new("non-existent-id", TestDataFactory.UpdateProductDto())
        };
        _repoMock.Setup(r => r.GetByIdAsync(p1.Id, default)).ReturnsAsync(p1);
        _repoMock.Setup(r => r.GetByIdAsync("non-existent-id", default)).ReturnsAsync((Product?)null);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new BulkUpdateProductsCommand(updates), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithEmptyList_ShouldReturnZero()
    {
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new BulkUpdateProductsCommand([]), default);

        result.Should().Be(0);
        _repoMock.Verify(r => r.BulkUpdateAsync(It.IsAny<IEnumerable<Product>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithAllNonExistentProducts_ShouldReturnZero()
    {
        var updates = new List<BulkUpdateProductDto>
        {
            new("bad-id-1", TestDataFactory.UpdateProductDto()),
            new("bad-id-2", TestDataFactory.UpdateProductDto())
        };
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<string>(), default)).ReturnsAsync((Product?)null);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(0);

        var result = await _handler.Handle(new BulkUpdateProductsCommand(updates), default);

        result.Should().Be(0);
    }
}
