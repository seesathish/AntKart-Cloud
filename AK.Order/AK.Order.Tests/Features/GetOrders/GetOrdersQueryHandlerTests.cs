using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.GetOrders;
using AK.Order.Domain.Common;
using AK.Order.Domain.Entities;
using OrderEntity = AK.Order.Domain.Entities.Order;
using AK.Order.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.GetOrders;

public class GetOrdersQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();

    public GetOrdersQueryHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        var orders = new List<OrderEntity> { TestDataFactory.CreateOrder(), TestDataFactory.CreateOrder(userId: "user-2") };
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        _repo.Setup(r => r.CountAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var handler = new GetOrdersQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrdersQuery(1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task Handle_EmptyResults_ReturnsEmptyPage()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.CountAsync(It.IsAny<ISpecification<OrderEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new GetOrdersQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrdersQuery(1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
