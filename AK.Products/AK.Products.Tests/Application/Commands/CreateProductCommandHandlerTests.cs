using AK.Products.Domain.Entities;
using AK.Products.Application.Commands.CreateProduct;
using AK.Products.Application.Interfaces;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Commands;

public sealed class CreateProductCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _handler = new CreateProductCommandHandler(_uowMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateAndReturnProduct()
    {
        var dto = TestDataFactory.CreateProductDto();
        _repoMock.Setup(r => r.SkuExistsAsync(dto.SKU, default)).ReturnsAsync(false);
        _repoMock.Setup(r => r.AddAsync(It.IsAny<Product>(), default))
            .ReturnsAsync((Product p, CancellationToken _) => p);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new CreateProductCommand(dto), default);

        result.Should().NotBeNull();
        result.Name.Should().Be(dto.Name);
        result.SKU.Should().Be(dto.SKU);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<Product>(), default), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateSku_ShouldThrowInvalidOperationException()
    {
        var dto = TestDataFactory.CreateProductDto();
        _repoMock.Setup(r => r.SkuExistsAsync(dto.SKU, default)).ReturnsAsync(true);

        var act = () => _handler.Handle(new CreateProductCommand(dto), default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{dto.SKU}*");
    }
}
