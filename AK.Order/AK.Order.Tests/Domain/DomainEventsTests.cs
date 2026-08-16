using AK.Order.Domain.Enums;
using AK.Order.Domain.Events;
using FluentAssertions;

namespace AK.Order.Tests.Domain;

public class DomainEventsTests
{
    [Fact]
    public void OrderCreatedEvent_HoldsCorrectData()
    {
        var id = Guid.NewGuid();
        var evt = new OrderCreatedEvent(id, "user-1", "ORD-20260418-ABCD1234");
        evt.OrderId.Should().Be(id);
        evt.UserId.Should().Be("user-1");
        evt.OrderNumber.Should().Be("ORD-20260418-ABCD1234");
    }

    [Fact]
    public void OrderStatusChangedEvent_HoldsCorrectData()
    {
        var id = Guid.NewGuid();
        var evt = new OrderStatusChangedEvent(id, OrderStatus.Pending, OrderStatus.Processing);
        evt.OrderId.Should().Be(id);
        evt.OldStatus.Should().Be(OrderStatus.Pending);
        evt.NewStatus.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public void OrderCancelledEvent_HoldsCorrectData()
    {
        var id = Guid.NewGuid();
        var evt = new OrderCancelledEvent(id, "user-1", "a@b.com", "A B", "ORD-20260418-ABCD1234");
        evt.OrderId.Should().Be(id);
        evt.UserId.Should().Be("user-1");
        evt.CustomerEmail.Should().Be("a@b.com");
        evt.CustomerName.Should().Be("A B");
        evt.OrderNumber.Should().Be("ORD-20260418-ABCD1234");
    }

    [Fact]
    public void OrderCreatedEvent_ValueEquality()
    {
        var id = Guid.NewGuid();
        var e1 = new OrderCreatedEvent(id, "user-1", "ORD-X");
        var e2 = new OrderCreatedEvent(id, "user-1", "ORD-X");
        e1.Should().Be(e2);
    }
}
