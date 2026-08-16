using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.GetOrdersByUser;
using AK.Order.Domain.Common;
using AK.Order.Domain.Entities;
using OrderEntity = AK.Order.Domain.Entities.Order;
using AK.Order.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.GetOrdersByUser;

public class GetOrdersByUserQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();

    public GetOrdersByUserQueryHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
    }

    [Fact]
    public async Task Handle_UserWithOrders_ReturnsPagedResult()
    {
        var order = TestDataFactory.CreateOrder(userId: "user-1");
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([order]);
        _repo.Setup(r => r.CountAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new GetOrdersByUserQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrdersByUserQuery("user-1"), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.Items[0].UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task Handle_UserWithNoOrders_ReturnsEmptyPage()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.CountAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new GetOrdersByUserQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrdersByUserQuery("user-none"), CancellationToken.None);

        result.Items.Should().BeEmpty();
    }
}
