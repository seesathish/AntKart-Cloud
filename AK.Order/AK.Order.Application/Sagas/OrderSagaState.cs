using MassTransit;

namespace AK.Order.Application.Sagas;

// Represents the persisted state of a single order SAGA instance.
// MassTransit stores one row per in-flight order in the OrderSagaStates table.
// When a new message arrives, MassTransit looks up the row by CorrelationId
// (= OrderId), rehydrates this object, runs the state machine logic, then saves it back.
public sealed class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    // CorrelationId links all messages that belong to the same saga instance.
    // We set this equal to OrderId so every event for the same order updates the same row.
    public Guid CorrelationId { get; set; }

    // Optimistic concurrency version — incremented on each save to prevent lost updates
    // when two messages for the same order arrive simultaneously.
    public int Version { get; set; }

    // The current state name as a string (e.g. "Initial", "StockPending", "Confirmed").
    // MassTransit writes this column; the state machine reads it to know which transitions apply.
    public string CurrentState { get; set; } = null!;

    // Business data copied from the OrderCreatedIntegrationEvent and carried forward
    // so downstream events (Confirmed / Cancelled) can be published with full context
    // without needing to query the Orders database.
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}
