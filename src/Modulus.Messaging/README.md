# Modulus.Messaging

Messaging library for .NET modular monoliths: integration events over an in-house transport layer with a transactional outbox and inbox. This core package includes the **in-memory transport**; RabbitMQ and Azure Service Bus ship as separate transport packages.

## Installation

```bash
dotnet add package ModulusKit.Messaging
```

For a broker transport, add the matching package and register it with one line:

| Transport | Package | Registration |
|---|---|---|
| RabbitMQ | `ModulusKit.Messaging.RabbitMq` | `services.AddModulusRabbitMqTransport();` |
| Azure Service Bus | `ModulusKit.Messaging.AzureServiceBus` | `services.AddModulusAzureServiceBusTransport();` |

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
configure everything imperatively via `AddModulusMessaging(Action<MessagingOptions>)`.

### Transport Configuration

Select the transport via the `Messaging` section — `"InMemory"`, `"RabbitMq"`, or `"AzureServiceBus"`:

```json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "...",
    "EndpointName": "orders-service",
    "PrefetchCount": 10,
    "AutoProvision": true
  }
}
```

```csharp
services.AddModulusRabbitMqTransport(); // from ModulusKit.Messaging.RabbitMq
services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

The RabbitMQ connection string format is `amqp://user:pass@host:5672/vhost`; Azure Service Bus takes
either an `Endpoint=sb://...` connection string or `FullyQualifiedNamespace` plus a `TokenCredential`
set in the callback (managed identity). Keep credentials in user secrets or environment variables.

If configuration selects a broker transport whose package is not registered, the host fails at
startup with guidance on which package to install.

Key options (all bindable from the `Messaging` section):

- `EndpointName` — queue/subscription identity of this host; defaults to the sanitized entry assembly name. Replicas sharing it compete for messages.
- `PrefetchCount` — messages delivered ahead of acknowledgement (default 10, range 1–1000).
- `AutoProvision` — declare topology automatically (default `true`); set `false` with pre-created entities for least privilege.
- `OutboxPollInterval` / `OutboxBatchSize` — outbox processor cadence (defaults: 5 seconds / 100).
- `RetryPolicy:*` (outbox dispatch) and `ConsumerRetry:*` (in-process consumer retry) — independent exponential-backoff policies.

## Publishing Events

```csharp
public record OrderShipped(Guid OrderId, DateTime ShippedAt)
    : IntegrationEvent;

// Publish directly via the message bus
await messageBus.Publish(new OrderShipped(orderId, DateTime.UtcNow));

// Or store in the outbox for reliable delivery
await outboxStore.Save(new OrderShipped(orderId, DateTime.UtcNow));
```

When using the outbox, events are stored in your database within the same transaction as your business data. A background `OutboxProcessor` polls for pending messages and publishes them through the configured transport, retrying per `RetryPolicy` before dead-lettering.

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

Handlers are auto-discovered from the assemblies you provide in `MessagingOptions.Assemblies` and registered as scoped services. All registered handlers for an event type are invoked; with the inbox registered, each handler runs at most once per event.

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

Switching between in-memory, RabbitMQ, and Azure Service Bus is a configuration change — flip the `Messaging` section's `Transport` value and supply the matching connection settings (with the transport package registered). No code changes are needed in your handlers or publishers.

## Learn More

See the [Modulus documentation](https://adamwyatt34.github.io/Modulus/messaging/) for the full messaging reference, including per-transport topology and the MassTransit migration guide.
