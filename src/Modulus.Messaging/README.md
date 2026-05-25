# Modulus.Messaging

Messaging library for .NET modular monoliths with MassTransit integration, supporting RabbitMQ, Azure Service Bus, and in-memory transports with a transactional outbox.

## Installation

```bash
dotnet add package ModulusKit.Messaging
```

## Setup

Bind the `Messaging` section from configuration — this is the section `modulus init --transport`
scaffolds into `appsettings.json`. The callback supplies the handler assemblies and any Azure
credential, which cannot be bound from configuration:

```json
// appsettings.json
{
  "Messaging": {
    "Transport": "InMemory"
  }
}
```

```csharp
services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

The callback runs after binding, so it can also override any bound value. Prefer this overload so
transport, connection string, and outbox/retry settings live in configuration. You can also
configure everything imperatively in code:

```csharp
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

### Transport Configuration

Set these via the `Messaging` section in `appsettings.json`:

```json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "amqp://guest:guest@localhost:5672"
  }
}
```

Or imperatively:

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

## Database setup

The outbox and inbox tables live in the `OutboxDbContext` and `InboxDbContext` that ship with this package. The package itself is **provider-agnostic** — you pick the EF Core provider (SQL Server, PostgreSQL, SQLite, etc.) in your host project and generate the migrations once against your chosen provider.

See [`Migrations/README.md`](https://github.com/adamwyatt34/Modulus/blob/main/src/Modulus.Messaging/Migrations/README.md) in the repository for the full workflow. The short version:

```csharp
builder.Services.AddModulusOutbox(o => o.UseSqlServer(connectionString));
builder.Services.AddModulusInbox(o => o.UseSqlServer(connectionString));

var app = builder.Build();
await app.UseModulusMessagingMigrationsAsync(); // applies pending migrations safely
app.Run();
```

## Switching Transports

To switch from in-memory to RabbitMQ or Azure Service Bus, change the `Transport` property and provide a connection string. No code changes are needed in your handlers or publishers — the transport is fully abstracted.

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
