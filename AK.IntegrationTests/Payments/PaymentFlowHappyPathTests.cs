using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.Payments;

/// <summary>
/// Happy-path payment event bus tests.
/// Uses in-memory MassTransit harness — no broker, no DB.
/// </summary>
public sealed class PaymentFlowHappyPathTests : IAsyncLifetime
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
    public async Task PaymentInitiated_IsPublishedAndConsumed()
    {
        var evt = IntegrationTestData.CreatePaymentInitiatedEvent();

        await _harness.Bus.Publish(evt);
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentInitiatedIntegrationEvent>()).Should().BeTrue(
            "PaymentInitiatedIntegrationEvent should be consumed by the bus");
    }

    [Fact]
    public async Task PaymentSucceeded_IsConsumedByPaymentSucceededConsumer()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId, orderId));
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.PaymentId == paymentId &&
                 m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "PaymentSucceededConsumer should consume the PaymentSucceededIntegrationEvent");
    }

    [Fact]
    public async Task PaymentSucceeded_DoesNotPublishPaymentFailed()
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId, orderId));
        await Task.Delay(400);

        (await _harness.Published.Any<PaymentFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "a succeeded payment must never trigger a PaymentFailed event");
    }

    [Fact]
    public async Task FullE2E_OrderCreated_StockReserved_OrderConfirmed_PaymentSucceeded()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var userId = "e2e-happy-user";

        // Phase 1 — order flow
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId, userId));
        await Task.Delay(400);

        // Assert SAGA produced OrderConfirmed
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "SAGA must publish OrderConfirmedIntegrationEvent after stock is reserved");

        // Phase 2 — payment flow
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(paymentId, orderId, userId));
        await Task.Delay(400);

        // Assert payment succeeded event was consumed
        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue(
            "PaymentSucceededConsumer should process the event for the confirmed order");

        // Assert no cancellation anywhere in the flow
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "a successful payment path must not produce any cancellation");
    }

    [Fact]
    public async Task PaymentInitiated_CorrectAmountAndCurrency()
    {
        var evt = IntegrationTestData.CreatePaymentInitiatedEvent(amount: 1499.00m);

        await _harness.Bus.Publish(evt);
        await Task.Delay(400);

        (await _harness.Consumed.Any<PaymentInitiatedIntegrationEvent>(
            m => m.Context.Message.Amount == 1499.00m &&
                 m.Context.Message.Currency == "INR")).Should().BeTrue(
            "amount and currency must be preserved through the event bus");
    }
}
