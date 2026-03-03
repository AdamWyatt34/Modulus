# Modulus.Messaging

Messaging library for .NET modular monoliths with MassTransit integration, supporting RabbitMQ, Azure Service Bus, and in-memory transports with a transactional outbox.

## Installation

```bash
dotnet add package ModulusKit.Messaging
```

## Setup

```csharp
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

### Transport Configuration

```csharp
// RabbitMQ
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://guest:guest@localhost:5672";
    options.Assemblies.Add(typeof(Program).Assembly);
});

// Azure Service Bus
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.AzureServiceBus;
    options.ConnectionString = "Endpoint=sb://...";
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

### Outbox Options

```csharp
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://...";
    options.OutboxPollInterval = TimeSpan.FromSeconds(5); // default
    options.OutboxBatchSize = 100;                        // default
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

## Publishing Events

```csharp
public record OrderShipped(Guid OrderId, DateTime ShippedAt)
    : IntegrationEvent;

// Publish directly via the message bus
await messageBus.Publish(new OrderShipped(orderId, DateTime.UtcNow));

// Or store in the outbox for reliable delivery
await outboxStore.Save(new OrderShipped(orderId, DateTime.UtcNow));
```

When using the outbox, events are stored in your database within the same transaction as your business data. A background `OutboxProcessor` polls for pending messages and publishes them via MassTransit.

## Handling Events

```csharp
public class OrderShippedHandler : IIntegrationEventHandler<OrderShipped>
{
    public async Task Handle(OrderShipped @event, CancellationToken ct)
    {
        // Handle the cross-module event
    }
}
```

Handlers are auto-discovered from the assemblies you provide in `MessagingOptions.Assemblies` and registered as scoped services.

## Switching Transports

To switch from in-memory to RabbitMQ or Azure Service Bus, change the `Transport` property and provide a connection string. No code changes are needed in your handlers or publishers — the transport is fully abstracted.

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
