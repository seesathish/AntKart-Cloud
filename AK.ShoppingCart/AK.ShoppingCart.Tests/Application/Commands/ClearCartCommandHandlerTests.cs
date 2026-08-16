using AK.ShoppingCart.Application.Commands.ClearCart;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.ShoppingCart.Tests.Application.Commands;

public sealed class ClearCartCommandHandlerTests
{
    private static (ClearCartCommandHandler handler, Mock<ICartRepository> repo) Create()
    {
        var repo = new Mock<ICartRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Carts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new ClearCartCommandHandler(uow.Object), repo);
    }

    [Fact]
    public async Task Handle_ExistingCart_ShouldClearAndReturnTrue()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithMultipleItems();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new ClearCartCommand(TestDataFactory.DefaultUserId), default);

        result.Should().BeTrue();
        repo.Verify(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CartNotFound_ShouldReturnFalse()
    {
        var (handler, repo) = Create();
        repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cart?)null);

        var result = await handler.Handle(new ClearCartCommand("nonexistent-user"), default);

        result.Should().BeFalse();
        repo.Verify(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
