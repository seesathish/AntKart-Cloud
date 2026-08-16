using AK.BuildingBlocks.Messaging.IntegrationEvents;
using MassTransit;

namespace AK.IntegrationTests.Common;

// Test-only no-op consumer that simulates an audit/notification service consuming PaymentInitiated.
// Allows harness.Consumed.Any<PaymentInitiatedIntegrationEvent>() assertions to pass.
public sealed class PaymentInitiatedAuditConsumer : IConsumer<PaymentInitiatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<PaymentInitiatedIntegrationEvent> context) => Task.CompletedTask;
}
