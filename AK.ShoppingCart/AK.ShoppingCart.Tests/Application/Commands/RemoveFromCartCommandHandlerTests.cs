using AK.ShoppingCart.Application.Commands.RemoveFromCart;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.ShoppingCart.Tests.Application.Commands;

public sealed class RemoveFromCartCommandHandlerTests
{
    private static (RemoveFromCartCommandHandler handler, Mock<ICartRepository> repo) Create()
    {
        var repo = new Mock<ICartRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Carts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new RemoveFromCartCommandHandler(uow.Object), repo);
    }

    [Fact]
    public async Task Handle_ExistingProduct_ShouldRemoveItemAndReturnDto()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new RemoveFromCartCommand(TestDataFactory.DefaultUserId, TestDataFactory.DefaultProductId), default);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CartNotFound_ShouldThrowKeyNotFoundException()
    {
        var (handler, repo) = Create();
        repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cart?)null);

        var act = async () => await handler.Handle(new RemoveFromCartCommand("user-x", "prod-x"), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ProductNotInCart_ShouldThrowKeyNotFoundException()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateEmptyCart();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var act = async () => await handler.Handle(new RemoveFromCartCommand(TestDataFactory.DefaultUserId, "nonexistent"), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
