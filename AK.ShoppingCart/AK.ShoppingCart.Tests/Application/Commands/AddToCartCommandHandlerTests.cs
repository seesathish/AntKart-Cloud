using AK.ShoppingCart.Application.Commands.AddToCart;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.ShoppingCart.Tests.Application.Commands;

public sealed class AddToCartCommandHandlerTests
{
    private static (AddToCartCommandHandler handler, Mock<IUnitOfWork> uow, Mock<ICartRepository> repo) Create()
    {
        var repo = new Mock<ICartRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Carts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new AddToCartCommandHandler(uow.Object), uow, repo);
    }

    [Fact]
    public async Task Handle_NewCart_ShouldCreateCartAndReturnDto()
    {
        var (handler, uow, repo) = Create();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cart?)null);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new AddToCartCommand(TestDataFactory.DefaultUserId, TestDataFactory.CreateAddCartItemDto());
        var result = await handler.Handle(cmd, default);

        result.UserId.Should().Be(TestDataFactory.DefaultUserId);
        result.Items.Should().HaveCount(1);
        result.TotalItems.Should().Be(TestDataFactory.DefaultQuantity);
        repo.Verify(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingCart_ShouldAddItemAndReturnDto()
    {
        var (handler, uow, repo) = Create();
        var existingCart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new AddToCartCommand(TestDataFactory.DefaultUserId, TestDataFactory.CreateAddCartItemDto("prod-999"));
        var result = await handler.Handle(cmd, default);

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AddingSameProduct_ShouldIncrementQuantity()
    {
        var (handler, uow, repo) = Create();
        var existingCart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCart);
        repo.Setup(r => r.SaveAsync(It.IsAny<Cart>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new AddToCartCommand(TestDataFactory.DefaultUserId, TestDataFactory.CreateAddCartItemDto(TestDataFactory.DefaultProductId, 3));
        var result = await handler.Handle(cmd, default);

        result.Items.Should().HaveCount(1);
        result.Items[0].Quantity.Should().Be(TestDataFactory.DefaultQuantity + 3);
    }
}
