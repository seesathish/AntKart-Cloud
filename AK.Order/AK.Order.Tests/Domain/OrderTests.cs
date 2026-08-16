using AK.Order.Domain.Entities;
using AK.Order.Domain.Enums;
using AK.Order.Domain.Events;
using AK.Order.Tests.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;
using FluentAssertions;

namespace AK.Order.Tests.Domain;

public class OrderTests
{
    [Fact]
    public void Create_ValidInputs_ReturnsOrderWithPendingStatus()
    {
        var order = TestDataFactory.CreateOrder();

        order.Should().NotBeNull();
        order.Status.Should().Be(OrderStatus.Pending);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        order.OrderNumber.Should().StartWith("ORD-");
        order.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Create_EmptyUserId_ThrowsArgumentException()
    {
        var act = () => TestDataFactory.CreateOrder(userId: "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullShippingAddress_ThrowsArgumentNullException()
    {
        var item = TestDataFactory.CreateOrderItem();
        var act = () => OrderEntity.Create("user-1", "a@b.com", "A B", null!, [item]);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NoItems_ThrowsArgumentException()
    {
        var addr = TestDataFactory.CreateShippingAddress();
        var act = () => OrderEntity.Create("user-1", "a@b.com", "A B", addr, []);
        act.Should().Throw<ArgumentException>().WithMessage("*at least one item*");
    }

    [Fact]
    public void Create_StoresCustomerEmailAndName()
    {
        var order = TestDataFactory.CreateOrder(customerEmail: "test@antkart.com", customerName: "Test User");
        order.CustomerEmail.Should().Be("test@antkart.com");
        order.CustomerName.Should().Be("Test User");
    }

    [Fact]
    public void Create_RaisesOrderCreatedEvent()
    {
        var order = TestDataFactory.CreateOrder();
        order.DomainEvents.Should().ContainSingle(e => e is OrderCreatedEvent);
    }

    [Fact]
    public void Create_OrderNumber_HasCorrectFormat()
    {
        var order = TestDataFactory.CreateOrder();
        order.OrderNumber.Should().MatchRegex(@"^ORD-\d{8}-[A-F0-9]{8}$");
    }

    [Fact]
    public void TotalAmount_CalculatesCorrectly()
    {
        var items = new List<OrderItem>
        {
            TestDataFactory.CreateOrderItem(price: 10m, quantity: 2),
            TestDataFactory.CreateOrderItem(productId: "prod-002", price: 5m, quantity: 3)
        };
        var order = TestDataFactory.CreateOrder(items: items);
        order.TotalAmount.Should().Be(35m);
    }

    [Fact]
    public void TotalItems_CalculatesCorrectly()
    {
        var items = new List<OrderItem>
        {
            TestDataFactory.CreateOrderItem(quantity: 2),
            TestDataFactory.CreateOrderItem(productId: "prod-002", quantity: 3)
        };
        var order = TestDataFactory.CreateOrder(items: items);
        order.TotalItems.Should().Be(5);
    }

    [Fact]
    public void UpdateStatus_ValidTransition_UpdatesStatus()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateStatus_RaisesOrderStatusChangedEvent()
    {
        var order = TestDataFactory.CreateOrder();
        order.ClearDomainEvents();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.DomainEvents.Should().ContainSingle(e => e is OrderStatusChangedEvent);
    }

    [Fact]
    public void UpdateStatus_SameStatus_DoesNotRaiseEvent()
    {
        var order = TestDataFactory.CreateOrder();
        order.ClearDomainEvents();
        order.UpdateStatus(OrderStatus.Pending);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateStatus_CancelledOrder_ThrowsInvalidOperationException()
    {
        var order = TestDataFactory.CreateOrder();
        order.Cancel();
        var act = () => order.UpdateStatus(OrderStatus.Processing);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cancelled*");
    }

    [Fact]
    public void Cancel_PendingOrder_SetsCancelledStatus()
    {
        var order = TestDataFactory.CreateOrder();
        order.Cancel();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ThrowsInvalidOperationException()
    {
        var order = TestDataFactory.CreateOrder();
        order.Cancel();
        var act = () => order.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already cancelled*");
    }

    [Fact]
    public void Cancel_DeliveredOrder_ThrowsInvalidOperationException()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Shipped);
        order.UpdateStatus(OrderStatus.Delivered);
        var act = () => order.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*delivered*");
    }

    [Fact]
    public void Cancel_RaisesOrderCancelledEvent()
    {
        var order = TestDataFactory.CreateOrder();
        order.ClearDomainEvents();
        order.Cancel();
        order.DomainEvents.Should().ContainSingle(e => e is OrderCancelledEvent);
    }

    [Fact]
    public void ConfirmPayment_SetsPaidStatus()
    {
        var order = TestDataFactory.CreateOrder();
        order.ConfirmPayment();
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
        order.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void AddItem_NewProduct_AddsToItems()
    {
        var order = TestDataFactory.CreateOrder();
        var newItem = TestDataFactory.CreateOrderItem(productId: "prod-999", productName: "New Product");
        order.AddItem(newItem);
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public void AddItem_ExistingProduct_IncrementsQuantity()
    {
        var order = TestDataFactory.CreateOrder();
        var initialQuantity = order.Items[0].Quantity;
        var sameItem = TestDataFactory.CreateOrderItem(productId: "prod-001", quantity: 3);
        order.AddItem(sameItem);
        order.Items.Should().HaveCount(1);
        order.Items[0].Quantity.Should().Be(initialQuantity + 3);
    }

    [Fact]
    public void AddItem_NullItem_ThrowsArgumentNullException()
    {
        var order = TestDataFactory.CreateOrder();
        var act = () => order.AddItem(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var order = TestDataFactory.CreateOrder();
        order.DomainEvents.Should().NotBeEmpty();
        order.ClearDomainEvents();
        order.DomainEvents.Should().BeEmpty();
    }

    // --- Payment transition state machine ---
    // Regression tests for the corrected _allowedTransitions table. By the time a payment
    // outcome is processed the order is in Confirmed (OrderConfirmedConsumer set it after the
    // saga reserved stock), so BOTH outcomes must be reachable from Confirmed. These fail
    // against the previous table (Confirmed omitted Paid/PaymentFailed; the Paid row was reversed).

    [Fact]
    public void UpdateStatus_ConfirmedToPaid_Succeeds()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Paid);
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void UpdateStatus_ConfirmedToPaid_ThenConfirmPayment_SetsPaymentStatusPaid()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Paid);
        order.ConfirmPayment();
        order.Status.Should().Be(OrderStatus.Paid);
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void UpdateStatus_ConfirmedToPaymentFailed_Succeeds()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.PaymentFailed);
        order.Status.Should().Be(OrderStatus.PaymentFailed);
    }

    [Fact]
    public void UpdateStatus_PaidToConfirmed_IsRejected()
    {
        // Paid moves FORWARD into fulfilment, never back to Confirmed. Guards against the
        // reversed [Paid] = [Confirmed] row returning.
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Paid);
        var act = () => order.UpdateStatus(OrderStatus.Confirmed);
        act.Should().Throw<InvalidOperationException>().WithMessage("*from Paid to Confirmed*");
    }

    [Fact]
    public void UpdateStatus_PaymentFailedToPaid_Succeeds()
    {
        // Retry path: a customer whose first payment failed can pay again and succeed.
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.PaymentFailed);
        order.UpdateStatus(OrderStatus.Paid);
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void UpdateStatus_DeliveredIsTerminal_RejectsFurtherTransition()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Shipped);
        order.UpdateStatus(OrderStatus.Delivered);
        var act = () => order.UpdateStatus(OrderStatus.Processing);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateStatus_CancelledIsTerminal_RejectsFurtherTransition()
    {
        var order = TestDataFactory.CreateOrder();
        order.UpdateStatus(OrderStatus.Confirmed);
        order.UpdateStatus(OrderStatus.Cancelled);
        var act = () => order.UpdateStatus(OrderStatus.Paid);
        act.Should().Throw<InvalidOperationException>();
    }
}
