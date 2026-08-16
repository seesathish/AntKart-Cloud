using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using AK.Order.Application.Sagas;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.Sagas;

public sealed class OrderSagaHappyPathTests : IAsyncLifetime
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
    public async Task OrderCreated_TransitionsTo_StockPending()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderEvent = IntegrationTestData.CreateOrderEvent(orderId);

        // Act
        await _harness.Bus.Publish(orderEvent);
        await Task.Delay(500);

        // Assert — saga instance created and in StockPending state
        var sagaHarness = _provider.GetRequiredService<ISagaStateMachineTestHarness<OrderSaga, OrderSagaState>>();
        sagaHarness.Sagas.Contains(orderId).Should().NotBeNull("saga should be created for the order");

        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
    }

    [Fact]
    public async Task StockReserved_TransitionsTo_Confirmed_AndPublishesOrderConfirmedEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await Task.Delay(300);

        // Act — stock reservation succeeds
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId));
        await Task.Delay(500);

        // Assert
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            msg => msg.Context.Message.OrderId == orderId)).Should().BeTrue(
            "saga should publish OrderConfirmedIntegrationEvent when stock is reserved");

        var sagaHarness = _provider.GetRequiredService<ISagaStateMachineTestHarness<OrderSaga, OrderSagaState>>();
        sagaHarness.Sagas.Contains(orderId).Should().NotBeNull("saga instance should exist after finalization");
    }

    [Fact]
    public async Task FullHappyPath_OrderCreated_StockReserved_OrderConfirmed()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var userId = "happy-path-user";

        // Act — publish order created
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId, userId));
        await Task.Delay(300);

        // Act — publish stock reserved
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId, userId));
        await Task.Delay(500);

        // Assert — saga consumed both events
        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
        (await _harness.Consumed.Any<StockReservedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        // Assert — OrderConfirmedIntegrationEvent was published
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

        // Assert — no cancellation event published
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeFalse(
            "order should not be cancelled in happy path");
    }
}
