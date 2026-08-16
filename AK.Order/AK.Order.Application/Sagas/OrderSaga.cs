using AK.BuildingBlocks.Messaging.IntegrationEvents;
using MassTransit;

namespace AK.Order.Application.Sagas;

// The OrderSaga orchestrates the order fulfilment flow across multiple services
// using messages over the bus — no direct HTTP calls between services.
//
// Flow:
//   1. OrderCreatedIntegrationEvent arrives  → store context, move to StockPending
//   2a. StockReservedIntegrationEvent arrives → publish OrderConfirmed, finalize
//   2b. StockReservationFailedIntegrationEvent → publish OrderCancelled, finalize
//
// MassTransit persists the saga state to PostgreSQL between steps so it survives
// a service restart between step 1 and step 2.
public sealed class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    // States the SAGA can be in. MassTransit persists the state name as a string column.
    public State StockPending { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    // Events the SAGA listens for. Each event is correlated to a saga instance by OrderId.
    public Event<OrderCreatedIntegrationEvent> OrderCreated { get; private set; } = null!;
    public Event<StockReservedIntegrationEvent> StockReserved { get; private set; } = null!;
    public Event<StockReservationFailedIntegrationEvent> StockReservationFailed { get; private set; } = null!;

    public OrderSaga()
    {
        // Tell MassTransit which property on the state object holds the current state name.
        InstanceState(x => x.CurrentState);

        // Correlation: when a message arrives, MassTransit finds the matching saga row by
        // comparing message.OrderId with the saga instance's CorrelationId column.
        Event(() => OrderCreated, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReservationFailed, e => e.CorrelateById(m => m.Message.OrderId));

        // Initially: only valid when no saga instance exists yet (state = Initial).
        // Copy all needed context from the event into saga state so later steps don't need to
        // query the Orders DB — the saga is self-contained.
        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    ctx.Saga.OrderId = ctx.Message.OrderId;
                    ctx.Saga.UserId = ctx.Message.UserId;
                    ctx.Saga.CustomerEmail = ctx.Message.CustomerEmail;
                    ctx.Saga.CustomerName = ctx.Message.CustomerName;
                    ctx.Saga.OrderNumber = ctx.Message.OrderNumber;
                    ctx.Saga.TotalAmount = ctx.Message.TotalAmount;
                })
                .TransitionTo(StockPending));

        // During StockPending: wait for AK.Products to respond with stock result.
        During(StockPending,
            When(StockReserved)
                // Stock was reserved successfully → confirm the order and trigger payment.
                .Publish(ctx => new OrderConfirmedIntegrationEvent(
                    ctx.Saga.OrderId,
                    ctx.Saga.UserId,
                    ctx.Saga.CustomerEmail,
                    ctx.Saga.CustomerName,
                    ctx.Saga.OrderNumber,
                    ctx.Saga.TotalAmount))
                .TransitionTo(Confirmed)
                .Finalize(),   // saga is done — MassTransit will delete the row

            When(StockReservationFailed)
                // Not enough stock → cancel the order and notify the customer.
                .Publish(ctx => new OrderCancelledIntegrationEvent(
                    ctx.Saga.OrderId,
                    ctx.Saga.UserId,
                    ctx.Saga.CustomerEmail,
                    ctx.Saga.CustomerName,
                    ctx.Saga.OrderNumber,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled)
                .Finalize());

        // Once Finalize() is called, MassTransit removes the saga row from the DB.
        SetCompletedWhenFinalized();
    }
}
