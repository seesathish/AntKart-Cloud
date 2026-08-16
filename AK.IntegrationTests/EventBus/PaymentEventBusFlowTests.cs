using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using AK.Order.Application.Sagas;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.EventBus;

/// <summary>
/// End-to-end async communication tests covering the full payment event bus flow.
/// Verifies that events route to the correct consumers and no cross-order pollution occurs.
/// Uses in-memory MassTransit harness — no broker, no DB.
/// </summary>
public sealed class PaymentEventBusFlowTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = TestHarnessFactory.CreateWithAllConsumers();
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task PaymentInitiatedEvent_IsConsumedByBus()
    {
        var evt = IntegrationTestData.CreatePaymentInitiatedEvent();

        await _harness.Bus.Publish(evt);
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentInitiatedIntegrationEvent>()).Should().BeTrue(
            "PaymentInitiatedIntegrationEvent should be routed and consumed");
    }

    [Fact]
    public async Task PaymentSucceededEvent_IsConsumedBySaga_AndNotCancelled()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId, orderId));
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.PaymentId == paymentId)).Should().BeTrue();

        (await _harness.Published.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "consuming a PaymentSucceeded event must not emit a failure");
    }

    [Fact]
    public async Task PaymentFailedEvent_IsConsumedBySaga_AndNotSucceeded()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(
            IntegrationTestData.CreatePaymentFailedEvent(paymentId, orderId, "Insufficient funds"));
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.PaymentId == paymentId)).Should().BeTrue();

        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "consuming a PaymentFailed event must not emit a success");
    }

    [Fact]
    public async Task FullOrderToPayment_HappyPath_AllEventsConsumed()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        const string userId = "e2e-full-happy";

        // Step 1 — place order
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);

        // Step 2 — reserve stock
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId, userId));
        await Task.Delay(400);

        // SAGA confirms order
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "SAGA must publish OrderConfirmed after stock is reserved");

        // Step 3 — payment succeeds
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId, orderId, userId));
        await Task.Delay(400);

        // All events consumed
        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
        (await _harness.Consumed.Any<StockReservedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        // Negative assertions — no failure events published
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse();
        (await _harness.Published.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse();
    }

    [Fact]
    public async Task FullOrderToPayment_SadPath_StockFails_PaymentNeverInitiated()
    {
        var orderId = Guid.NewGuid();
        const string userId = "e2e-stock-sad";

        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId, "Out of stock"));
        await Task.Delay(400);

        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "order must be cancelled when stock fails");

        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "order must not be confirmed if stock fails");
        (await _harness.Published.Any<PaymentInitiatedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "payment must never be initiated when order is cancelled");
    }

    [Fact]
    public async Task FullOrderToPayment_SadPath_PaymentFails_AllEventsConsumed()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        const string userId = "e2e-payment-sad";

        // Order succeeds through SAGA
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId, userId));
        await Task.Delay(400);

        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        // Payment fails
        await _harness.Bus.Publish(
            IntegrationTestData.CreatePaymentFailedEvent(paymentId, orderId, "Card expired"));
        await Task.Delay(400);

        // All events consumed
        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        // No success published
        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse();
    }

    [Fact]
    public async Task TwoConcurrentOrders_PaymentEventsIsolated()
    {
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var paymentId1 = Guid.NewGuid();
        var paymentId2 = Guid.NewGuid();

        // Both orders placed and stock reserved simultaneously
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId2));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId2));
        await Task.Delay(400);

        // Order 1 payment succeeds, Order 2 payment fails
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId1, orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentFailedEvent(paymentId2, orderId2));
        await Task.Delay(500);

        // Verify correct events consumed per order
        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeTrue("order 1 payment succeeded");
        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeTrue("order 2 payment failed");

        // Verify zero cross-contamination
        (await _harness.Published.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeFalse("order 1 must not fail");
        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeFalse("order 2 must not succeed");
    }
}
