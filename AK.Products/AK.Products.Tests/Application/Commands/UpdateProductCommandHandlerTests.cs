using AK.Products.Domain.Entities;
using AK.Products.Application.Commands.UpdateProduct;
using AK.Products.Application.Interfaces;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Commands;

public sealed class UpdateProductCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly UpdateProductCommandHandler _handler;

    public UpdateProductCommandHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _handler = new UpdateProductCommandHandler(_uowMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldUpdateAndReturn()
    {
        var product = TestDataFactory.CreateMenProduct();
        var updateDto = TestDataFactory.UpdateProductDto();
        _repoMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new UpdateProductCommand(product.Id, updateDto), default);

        result.Name.Should().Be(updateDto.Name);
        result.Price.Should().Be(updateDto.Price);
        _repoMock.Verify(r => r.UpdateAsync(product, default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldThrowKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByIdAsync("nonexistent", default))
            .ReturnsAsync((Product?)null);

        var act = () => _handler.Handle(
            new UpdateProductCommand("nonexistent", TestDataFactory.UpdateProductDto()), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
