using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Consumers;
using AK.Order.Domain.Enums;
using AK.Order.Tests.Common;
using FluentAssertions;
using MassTransit;
using Moq;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Tests.Application.Consumers;

public class OrderConfirmedConsumerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IEventGridSideEffectPublisher> _sideEffects = new();

    public OrderConfirmedConsumerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static ConsumeContext<OrderConfirmedIntegrationEvent> Context(OrderConfirmedIntegrationEvent evt)
    {
        var ctx = new Mock<ConsumeContext<OrderConfirmedIntegrationEvent>>();
        ctx.Setup(c => c.Message).Returns(evt);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task Consume_ConfirmsOrder_AndPublishesNotificationSideEffect()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _sideEffects
            .Setup(p => p.TryPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var evt = new OrderConfirmedIntegrationEvent(order.Id, "user-123", "a@b.com", "Alice", "ORD-1", 59.98m);
        var consumer = new OrderConfirmedConsumer(_uow.Object, _sideEffects.Object);

        await consumer.Consume(Context(evt));

        order.Status.Should().Be(OrderStatus.Confirmed);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sideEffects.Verify(p => p.TryPublishAsync(
            "AntKart.Order.Confirmed", It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenSideEffectPublishFails_StillConfirmsOrder()
    {
        // The publisher returns false (publish skipped/failed) — and never throws. The durable
        // confirmation must still succeed: the side-effect path is fully decoupled from the saga.
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _sideEffects
            .Setup(p => p.TryPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var evt = new OrderConfirmedIntegrationEvent(order.Id, "user-123", "a@b.com", "Alice", "ORD-1", 59.98m);
        var consumer = new OrderConfirmedConsumer(_uow.Object, _sideEffects.Object);

        var act = async () => await consumer.Consume(Context(evt));

        await act.Should().NotThrowAsync();
        order.Status.Should().Be(OrderStatus.Confirmed);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_DoesNothing()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((OrderEntity?)null);

        var evt = new OrderConfirmedIntegrationEvent(Guid.NewGuid(), "u", "a@b.com", "Alice", "ORD-1", 10m);
        var consumer = new OrderConfirmedConsumer(_uow.Object, _sideEffects.Object);

        await consumer.Consume(Context(evt));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _sideEffects.Verify(p => p.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
