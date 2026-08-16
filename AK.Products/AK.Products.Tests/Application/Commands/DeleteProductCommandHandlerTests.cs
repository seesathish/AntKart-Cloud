using AK.Products.Application.Commands.DeleteProduct;
using AK.Products.Application.Interfaces;
using AK.Products.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Products.Tests.Application.Commands;

public sealed class DeleteProductCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IProductRepository> _repoMock = new();
    private readonly DeleteProductCommandHandler _handler;

    public DeleteProductCommandHandlerTests()
    {
        _uowMock.Setup(u => u.Products).Returns(_repoMock.Object);
        _handler = new DeleteProductCommandHandler(_uowMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldDeleteAndReturnTrue()
    {
        var product = TestDataFactory.CreateMenProduct();
        _repoMock.Setup(r => r.ExistsAsync(product.Id, default)).ReturnsAsync(true);
        _uowMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new DeleteProductCommand(product.Id), default);

        result.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteAsync(product.Id, default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldThrowKeyNotFoundException()
    {
        _repoMock.Setup(r => r.ExistsAsync("bad-id", default)).ReturnsAsync(false);

        var act = () => _handler.Handle(new DeleteProductCommand("bad-id"), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
