using AK.ShoppingCart.Application.Commands.UpdateCartItem;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.ShoppingCart.Tests.Application.Commands;

public sealed class UpdateCartItemCommandHandlerTests
{
    private static (UpdateCartItemCommandHandler handler, Mock<ICartRepository> repo) Create()
    {
        var repo = new Mock<ICartRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Carts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new UpdateCartItemCommandHandler(uow.Object), repo);
    }

    [Fact]
    public async Task Handle_ValidQuantity_ShouldUpdateItemQuantity()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new UpdateCartItemCommand(TestDataFactory.DefaultUserId, TestDataFactory.DefaultProductId, 5), default);

        result.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    public async Task Handle_ZeroQuantity_ShouldRemoveItem()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new UpdateCartItemCommand(TestDataFactory.DefaultUserId, TestDataFactory.DefaultProductId, 0), default);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CartNotFound_ShouldThrowKeyNotFoundException()
    {
        var (handler, repo) = Create();
        repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cart?)null);

        var act = async () => await handler.Handle(new UpdateCartItemCommand("user-x", "prod-x", 1), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
