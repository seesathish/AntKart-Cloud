using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.Payments;

/// <summary>
/// Sad-path payment event bus tests — payment failures and stock failures.
/// Uses in-memory MassTransit harness — no broker, no DB.
/// </summary>
public sealed class PaymentFlowSadPathTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = TestHarnessFactory.CreateWithPaymentConsumers();
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task PaymentFailed_IsConsumedByPaymentFailedConsumer()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentFailedEvent(paymentId, orderId));
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.PaymentId == paymentId &&
                 m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "PaymentFailedConsumer should consume the PaymentFailedIntegrationEvent");
    }

    [Fact]
    public async Task PaymentFailed_DoesNotPublishPaymentSucceeded()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentFailedEvent(paymentId, orderId));
        await Task.Delay(400);

        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "a failed payment must never trigger a PaymentSucceeded event");
    }

    [Fact]
    public async Task PaymentFailed_PropagatesFailureReason()
    {
        var reason = "Card declined by issuing bank";
        var evt = IntegrationTestData.CreatePaymentFailedEvent(reason: reason);

        await _harness.Bus.Publish(evt);
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.Reason == reason)).Should().BeTrue(
            "failure reason must be carried through the event bus unchanged");
    }

    [Fact]
    public async Task FullE2E_OrderCreated_StockReserved_OrderConfirmed_PaymentFailed()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var userId = "e2e-sad-payment-user";

        // Phase 1 — order flow succeeds (stock reserved)
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId, userId));
        await Task.Delay(400);

        // SAGA confirms the order
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "SAGA must confirm the order before payment is attempted");

        // Phase 2 — payment fails
        await _harness.Bus.Publish(
            IntegrationTestData.CreatePaymentFailedEvent(paymentId, orderId, "Signature verification failed."));
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "PaymentFailedConsumer should process the failure event");

        // No payment success should have been published
        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "a failed payment must not produce a PaymentSucceeded event");
    }

    [Fact]
    public async Task FullE2E_OrderCreated_StockFailed_NeverReachesPayment()
    {
        var orderId = Guid.NewGuid();
        var userId = "e2e-stock-failed-user";

        // Order placed but stock fails
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId, "Out of stock"));
        await Task.Delay(400);

        // SAGA cancels the order — no payment should be initiated
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "SAGA must cancel the order when stock reservation fails");

        (await _harness.Published.Any<PaymentInitiatedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "payment must never be initiated when stock reservation fails");
    }

    [Fact]
    public async Task MultiplePayments_IsolatedFailures_DoNotCrossContaminate()
    {
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var paymentId1 = Guid.NewGuid();
        var paymentId2 = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId1, orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentFailedEvent(paymentId2, orderId2));
        await Task.Delay(500);

        // order 1 got succeeded event, order 2 got failed event
        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeTrue();
        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeTrue();

        // cross-contamination checks
        (await _harness.Published.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeFalse(
            "order 1 must not receive a failure event");
        (await _harness.Published.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeFalse(
            "order 2 must not receive a success event");
    }
}
