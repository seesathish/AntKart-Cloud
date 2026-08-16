using AK.Products.Domain.Entities;
using AK.Products.Application.Commands.BulkInsertProducts;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Commands;

public sealed class BulkInsertProductsCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly BulkInsertProductsCommandHandler _handler;

    public BulkInsertProductsCommandHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _handler = new BulkInsertProductsCommandHandler(_uowMock.Object);
    }

    [Fact]
    public async Task Handle_WithMultipleProducts_ShouldInsertAllAndReturnCount()
    {
        var dtos = new List<CreateProductDto>
        {
            TestDataFactory.CreateProductDto("SKU-001"),
            TestDataFactory.CreateProductDto("SKU-002"),
            TestDataFactory.CreateProductDto("SKU-003")
        };
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new BulkInsertProductsCommand(dtos), default);

        result.Should().Be(3);
        _repoMock.Verify(r => r.BulkInsertAsync(
            It.IsAny<IEnumerable<Product>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithEmptyList_ShouldReturnZero()
    {
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new BulkInsertProductsCommand([]), default);

        result.Should().Be(0);
    }
}
