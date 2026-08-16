namespace AK.BuildingBlocks.Messaging.EventGrid;

// Publishes a discrete, FIRE-AND-FORGET side-effect event to Azure Event Grid.
//
// This is deliberately SEPARATE from the durable Service Bus saga backbone. Event Grid + a
// serverless Function handle lightweight side-effects (e.g. a notification) that:
//   • must NOT be able to fail or delay the core transaction;
//   • scale to zero and are billed per execution.
//
// The decoupling guarantee is in the method contract: TryPublishAsync NEVER throws. A failure
// to publish (or a missing/invalid endpoint) returns false and is logged — it can never
// propagate back into the caller's transaction or roll back the saga.
public interface IEventGridSideEffectPublisher
{
    // Returns true if the event was published, false if publishing was skipped or failed.
    // Never throws.
    Task<bool> TryPublishAsync(string eventType, string subject, object data, CancellationToken ct = default);
}
