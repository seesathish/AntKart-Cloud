using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using AK.Order.Application.Consumers;
using AK.Order.Application.Sagas;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.EventBus;

public sealed class EventBusFlowTests : IAsyncLifetime
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
    public async Task OrderCreatedEvent_IsConsumedBySaga()
    {
        var evt = IntegrationTestData.CreateOrderEvent();

        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
    }

    [Fact]
    public async Task StockReservedEvent_IsConsumedBySaga_AndPublishesConfirmed()
    {
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await Task.Delay(300);

        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId));
        await Task.Delay(500);

        (await _harness.Consumed.Any<StockReservedIntegrationEvent>()).Should().BeTrue();
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
    }

    [Fact]
    public async Task StockFailedEvent_IsConsumedBySaga_AndPublishesCancelled()
    {
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await Task.Delay(300);

        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId));
        await Task.Delay(500);

        (await _harness.Consumed.Any<StockReservationFailedIntegrationEvent>()).Should().BeTrue();
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId)).Should().BeTrue();
    }

    [Fact]
    public async Task MultipleOrders_EachSagaIsIsolated()
    {
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId2));
        await Task.Delay(300);

        // order 1 succeeds, order 2 fails
        await _harness.Bus.Publish(IntegrationTestData.CreateStockReservedEvent(orderId1));
        await _harness.Bus.Publish(IntegrationTestData.CreateStockFailedEvent(orderId2));
        await Task.Delay(500);

        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeTrue("order 1 should confirm");
        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeTrue("order 2 should cancel");

        (await _harness.Published.Any<OrderCancelledIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId1)).Should().BeFalse("order 1 must not cancel");
        (await _harness.Published.Any<OrderConfirmedIntegrationEvent>(
            m => m.Context.Message.OrderId == orderId2)).Should().BeFalse("order 2 must not confirm");
    }
}
