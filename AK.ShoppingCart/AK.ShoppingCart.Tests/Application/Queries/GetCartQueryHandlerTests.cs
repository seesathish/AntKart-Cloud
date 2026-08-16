using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Application.Queries.GetCart;
using AK.ShoppingCart.Domain.Entities;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.ShoppingCart.Tests.Application.Queries;

public sealed class GetCartQueryHandlerTests
{
    private static (GetCartQueryHandler handler, Mock<ICartRepository> repo) Create()
    {
        var repo = new Mock<ICartRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Carts).Returns(repo.Object);
        return (new GetCartQueryHandler(uow.Object), repo);
    }

    [Fact]
    public async Task Handle_ExistingCart_ShouldReturnCartDto()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithMultipleItems();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var result = await handler.Handle(new GetCartQuery(TestDataFactory.DefaultUserId), default);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(TestDataFactory.DefaultUserId);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_CartNotFound_ShouldReturnNull()
    {
        var (handler, repo) = Create();
        repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cart?)null);

        var result = await handler.Handle(new GetCartQuery("nonexistent"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CartWithItems_DtoShouldHaveCorrectTotals()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateCartWithItem();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var result = await handler.Handle(new GetCartQuery(TestDataFactory.DefaultUserId), default);

        result!.TotalAmount.Should().Be(TestDataFactory.DefaultPrice * TestDataFactory.DefaultQuantity);
        result.TotalItems.Should().Be(TestDataFactory.DefaultQuantity);
        result.Items[0].SubTotal.Should().Be(TestDataFactory.DefaultPrice * TestDataFactory.DefaultQuantity);
    }

    [Fact]
    public async Task Handle_EmptyCart_ShouldReturnDtoWithZeroTotals()
    {
        var (handler, repo) = Create();
        var cart = TestDataFactory.CreateEmptyCart();
        repo.Setup(r => r.GetAsync(TestDataFactory.DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var result = await handler.Handle(new GetCartQuery(TestDataFactory.DefaultUserId), default);

        result!.TotalAmount.Should().Be(0);
        result.TotalItems.Should().Be(0);
        result.Items.Should().BeEmpty();
    }
}
