using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Consumers;
using AK.Order.Domain.Enums;
using AK.Order.Tests.Common;
using FluentAssertions;
using MassTransit;
using Moq;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Tests.Application.Consumers;

public class OrderCancelledConsumerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IEventGridSideEffectPublisher> _sideEffects = new();

    public OrderCancelledConsumerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _sideEffects.Setup(p => p.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static ConsumeContext<OrderCancelledIntegrationEvent> Context(OrderCancelledIntegrationEvent evt)
    {
        var ctx = new Mock<ConsumeContext<OrderCancelledIntegrationEvent>>();
        ctx.Setup(c => c.Message).Returns(evt);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static OrderCancelledIntegrationEvent Event(OrderEntity order) =>
        new(order.Id, "user-123", "alice@example.com", "Alice", order.OrderNumber, "Out of stock");

    [Fact]
    public async Task Consume_CancelsOrder_AndPublishesOrderCancelledNotification()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        OrderCancelledNotification? published = null;
        _sideEffects.Setup(p => p.TryPublishAsync(
                NotificationEventTypes.OrderCancelled, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object, CancellationToken>((_, _, data, _) => published = (OrderCancelledNotification)data)
            .ReturnsAsync(true);

        await new OrderCancelledConsumer(_uow.Object, _sideEffects.Object).Consume(Context(Event(order)));

        order.Status.Should().Be(OrderStatus.Cancelled);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        published.Should().NotBeNull();
        published!.CustomerEmail.Should().Be("alice@example.com");
        published.OrderNumber.Should().Be(order.OrderNumber);
        published.Reason.Should().Be("Out of stock");
    }

    [Fact]
    public async Task Consume_WhenNotificationPublishFails_StillCancels()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _sideEffects.Setup(p => p.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await new OrderCancelledConsumer(_uow.Object, _sideEffects.Object).Consume(Context(Event(order)));

        await act.Should().NotThrowAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_DoesNothing()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrderEntity?)null);

        var evt = new OrderCancelledIntegrationEvent(Guid.NewGuid(), "u", "a@b.com", "A", "ORD-1", "x");
        await new OrderCancelledConsumer(_uow.Object, _sideEffects.Object).Consume(Context(evt));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _sideEffects.Verify(p => p.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
