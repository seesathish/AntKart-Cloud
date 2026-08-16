using AK.BuildingBlocks.Messaging.EventGrid;

namespace AK.IntegrationTests.Common;

// Test double: a no-op side-effect publisher so consumers that depend on
// IEventGridSideEffectPublisher resolve in the transport-agnostic in-memory harness.
// The fire-and-forget Event Grid path itself is unit-tested separately with a mock.
public sealed class NoOpEventGridSideEffectPublisher : IEventGridSideEffectPublisher
{
    public Task<bool> TryPublishAsync(
        string eventType, string subject, object data, CancellationToken ct = default)
        => Task.FromResult(false);
}
