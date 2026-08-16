using AK.BuildingBlocks.Messaging.IntegrationEvents;
using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.BuildingBlocks.Messaging;

// Shared MassTransit + Azure Service Bus setup. Every service that needs the event bus calls
// AddAzureServiceBusMassTransit() from its Infrastructure ServiceCollectionExtensions.
//
// MassTransit abstracts the transport: consumers, the saga, and publish/send calls are written
// against MassTransit's API and are UNCHANGED by the change of underlying transport to Azure
// Service Bus — only this bus configuration changes.
//
// ── Topology is owned by infrastructure-as-code ─────────────────────────────────────────────
// The Service Bus topology is provisioned by IaC — the platform's single source of truth — and
// the application must CONFORM to it, never create or manage it. The provisioned entities are:
//   • a TOPIC  "integration-events" — every integration event is published here (a single,
//              shared topic), and each consuming service reads its OWN named SUBSCRIPTION on it;
//   • a QUEUE  "order-commands"     — for command-style messages with a single owner.
// The runtime identity holds only Azure Service Bus Data Sender / Data Receiver — never Manage —
// so this configuration:
//   • does NOT call ConfigureEndpoints (which would auto-create MassTransit's conventional
//     entities: a queue per consumer and a topic per message type);
//   • maps every integration-event type onto the single existing "integration-events" topic;
//   • binds ONLY explicit receive endpoints to the existing provisioned subscriptions / queue,
//     supplied by each service through the `configureReceiveEndpoints` callback.
// Adding a new consumer or event that needs a NEW subscription/queue is therefore an
// infrastructure change (the Service Bus Terraform module), not a runtime concern.
public static class MassTransitExtensions
{
    // The single, IaC-provisioned topic that carries every integration event.
    public const string IntegrationEventsTopic = "integration-events";

    public static IServiceCollection AddAzureServiceBusMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator> registerConsumers,
        Action<IBusRegistrationContext, IServiceBusBusFactoryConfigurator> configureReceiveEndpoints)
    {
        var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
            ?? throw new InvalidOperationException(
                "ServiceBus:FullyQualifiedNamespace is missing from configuration.");

        services.AddMassTransit(x =>
        {
            // Service-specific consumers / saga registered by the caller.
            registerConsumers(x);

            x.UsingAzureServiceBus((context, cfg) =>
            {
                // Entra (token) auth against the namespace — no connection string / SAS key.
                // DefaultAzureCredential resolves to the developer's Azure CLI sign-in locally and
                // the resource's managed identity in the cloud.
                cfg.Host(new Uri($"sb://{fullyQualifiedNamespace}"), host =>
                {
                    host.TokenCredential = new DefaultAzureCredential();
                });

                // SINGLE SHARED TOPIC. By default MassTransit publishes each message type to its
                // own topic (topic-per-message-type). The platform provisions ONE topic,
                // "integration-events", for all integration events. Map every integration-event
                // type onto that single topic, so publishing an event lands on the provisioned
                // topic and each service's subscription (declared below) is read from it.
                cfg.Message<OrderCreatedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<OrderConfirmedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<OrderCancelledIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<StockReservedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<StockReservationFailedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<PaymentInitiatedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<PaymentSucceededIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));
                cfg.Message<PaymentFailedIntegrationEvent>(m => m.SetEntityName(IntegrationEventsTopic));

                // Transport-agnostic message retry: on a transient consumer failure, retry 3 times
                // with incremental delays before the message dead-letters. Unchanged.
                cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

                // NO ConfigureEndpoints(context). Instead, the caller binds explicit receive
                // endpoints to the EXISTING provisioned entities (a subscription on the
                // integration-events topic, and/or the order-commands queue). The identity is
                // Send/Receive-only, so no entity is created or managed here.
                configureReceiveEndpoints(context, cfg);
            });
        });

        return services;
    }
}
