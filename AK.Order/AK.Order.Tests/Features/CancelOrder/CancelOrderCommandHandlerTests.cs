using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.CancelOrder;
using AK.Order.Domain.Entities;
using AK.Order.Domain.Enums;
using OrderEntity = AK.Order.Domain.Entities.Order;
using AK.Order.Tests.Common;
using FluentAssertions;
using MassTransit;
using Moq;

namespace AK.Order.Tests.Features.CancelOrder;

public class CancelOrderCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();

    public CancelOrderCommandHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_ValidOrder_ReturnsSuccessResult()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new CancelOrderCommandHandler(_uow.Object, _publisher.Object);
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidOrder_PublishesOrderCancelledEvent()
    {
        var order = TestDataFactory.CreateOrder(customerEmail: "john@example.com", customerName: "John Doe");
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new CancelOrderCommandHandler(_uow.Object, _publisher.Object);
        await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == order.Id &&
                e.CustomerEmail == "john@example.com" &&
                e.CustomerName == "John Doe"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsFailureResult()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrderEntity?)null);

        var handler = new CancelOrderCommandHandler(_uow.Object, _publisher.Object);
        var result = await handler.Handle(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AlreadyCancelled_ReturnsFailureResult()
    {
        var order = TestDataFactory.CreateOrder();
        order.Cancel();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = new CancelOrderCommandHandler(_uow.Object, _publisher.Object);
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already cancelled");
    }

    [Fact]
    public async Task Handle_DeliveredOrder_ReturnsFailureResult()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Processing);
        order.UpdateStatus(OrderStatus.Shipped);
        order.UpdateStatus(OrderStatus.Delivered);
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = new CancelOrderCommandHandler(_uow.Object, _publisher.Object);
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot cancel a delivered order");
    }
}
