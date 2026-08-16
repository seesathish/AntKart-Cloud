using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using AK.Order.Application.Sagas;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.Sagas;

public sealed class OrderSagaSadPathTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = TestHarnessFactory.CreateWithSaga();
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task StockReservationFailed_TransitionsTo_Cancelled_AndPublishesOrderCancelledEvent()
    {
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await Task.Delay(300);

        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId));
        await Task.Delay(500);

        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            msg => msg.Context.Message.OrderId == orderId)).Should().BeTrue(
            "saga should publish OrderCancelledIntegrationEvent when stock reservation fails");

        var sagaHarness = _provider.GetRequiredService<ISagaStateMachineTestHarness<OrderSaga, OrderSagaState>>();
        sagaHarness.Sagas.Contains(orderId).Should().NotBeNull("saga instance should exist after finalization");
    }

    [Fact]
    public async Task StockReservationFailed_DoesNotPublishOrderConfirmedEvent()
    {
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await Task.Delay(300);

        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId, "Out of stock"));
        await Task.Delay(500);

        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            msg => msg.Context.Message.OrderId == orderId)).Should().BeFalse(
            "confirmed event must not be published when stock fails");
    }

    [Fact]
    public async Task FullSadPath_OrderCreated_StockFailed_OrderCancelled()
    {
        var orderId = Guid.NewGuid();
        var userId = "sad-path-user";
        var failureReason = "Insufficient stock for: MEN-SHIR-001";

        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);

        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId, failureReason, userId));
        await Task.Delay(500);

        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
        (await _harness.Consumed.Any<StockReservationFailedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        var cancelledEvent = _harness.Published
            .Select<OrderCancelledIntegrationEvent>(m => m.Context.Message.OrderId == orderId)
            .First();
        cancelledEvent.Context.Message.Reason.Should().Be(failureReason);
    }
}
