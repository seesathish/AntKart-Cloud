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

public class PaymentSucceededConsumerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();

    public PaymentSucceededConsumerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static ConsumeContext<PaymentSucceededIntegrationEvent> Context(PaymentSucceededIntegrationEvent evt)
    {
        var ctx = new Mock<ConsumeContext<PaymentSucceededIntegrationEvent>>();
        ctx.Setup(c => c.Message).Returns(evt);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static PaymentSucceededIntegrationEvent Event(Guid orderId) =>
        new(Guid.NewGuid(), orderId, "user-123", "a@b.com", "Alice", "ORD-1", 59.98m, "pay_ABC123");

    [Fact]
    public async Task Consume_WhenOrderConfirmed_SetsPaidAndConfirmsPayment()
    {
        // The real scenario: the saga has confirmed the order, so it is in Confirmed when the
        // payment succeeds. The consumer must not throw — Confirmed -> Paid is a valid transition.
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var consumer = new PaymentSucceededConsumer(_uow.Object);

        var act = async () => await consumer.Consume(Context(Event(order.Id)));

        await act.Should().NotThrowAsync();
        order.Status.Should().Be(OrderStatus.Paid);
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_DoesNothing()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((OrderEntity?)null);

        var consumer = new PaymentSucceededConsumer(_uow.Object);

        await consumer.Consume(Context(Event(Guid.NewGuid())));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
