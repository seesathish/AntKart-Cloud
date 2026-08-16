using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.GetOrderById;
using AK.Order.Domain.Entities;
using OrderEntity = AK.Order.Domain.Entities.Order;
using AK.Order.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.GetOrderById;

public class GetOrderByIdQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();

    public GetOrderByIdQueryHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
    }

    [Fact]
    public async Task Handle_OrderExists_ReturnsDto()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = new GetOrderByIdQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(order.Id);
        result.UserId.Should().Be(order.UserId);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNull()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrderEntity?)null);

        var handler = new GetOrderByIdQueryHandler(_uow.Object);
        var result = await handler.Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }
}
