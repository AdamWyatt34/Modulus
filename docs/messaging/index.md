# Messaging Overview

Modulus provides an in-house messaging transport layer for cross-module communication via integration events. It supports InMemory, RabbitMQ, and Azure Service Bus transports, and includes built-in transactional outbox and inbox patterns for reliable, exactly-once message delivery. There is no MassTransit (or other framework) dependency -- the broker transports are built directly on the official `RabbitMQ.Client` and `Azure.Messaging.ServiceBus` clients.

## Why an In-House Transport Layer?

| Concern | Modulus Messaging |
|---|---|
| **Swap transports freely** | Switch between InMemory, RabbitMQ, and Azure Service Bus by changing a single configuration value. No handler code changes required. |
| **Clean handler interface** | Implement `IIntegrationEventHandler<TEvent>` -- no broker-specific consumer types or context ceremony. |
| **Automatic idempotency** | The consumer pipeline enforces at-most-once execution per `(EventId, handlerName)` when the inbox store is registered. |
| **Transactional outbox built-in** | Save domain state and outbox messages in the same database transaction. A background processor publishes them reliably to the broker. |
| **Convention-based discovery** | Handlers are auto-discovered from the assemblies you specify -- no manual consumer registration. |
| **No licensed dependencies** | The whole stack is MIT-licensed, built on the official broker client libraries. |

## Installation

If you scaffolded your solution with the Modulus CLI, the messaging package is already referenced. To add it manually:

```bash
dotnet add package ModulusKit.Messaging
```

The core package includes the in-memory transport. RabbitMQ and Azure Service Bus ship as separate transport packages -- `ModulusKit.Messaging.RabbitMq` and `ModulusKit.Messaging.AzureServiceBus` -- each activated with a one-line registration. See [Transports](./transports).

## Quick Setup

Register messaging in your host project's `Program.cs` or composition root. The recommended way is
to bind the `Messaging` section from configuration — this is the section `modulus init --transport`
scaffolds into `appsettings.json`:

```json
// appsettings.json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "amqp://guest:guest@localhost:5672"
  }
}
```

<!-- verify -->
```csharp
using Modulus.Messaging;
using Modulus.Messaging.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

// One line per broker transport (from ModulusKit.Messaging.RabbitMq);
// not needed for the built-in InMemory transport.
builder.Services.AddModulusRabbitMqTransport();

builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

The callback supplies values that cannot be bound from configuration — the handler assemblies and an
optional Azure `TokenCredential` — and runs after binding, so it can also override any bound value.

You can add multiple assemblies to scan for handlers across all your modules:

```csharp
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(CatalogModule).Assembly);
    options.Assemblies.Add(typeof(OrdersModule).Assembly);
    options.Assemblies.Add(typeof(PaymentModule).Assembly);
});
```

Or configure everything imperatively without a configuration section:

<!-- verify -->
```csharp
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

## MessagingOptions Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Transport` | `Transport` | `InMemory` | The message broker transport to use. One of `InMemory`, `RabbitMq`, or `AzureServiceBus`. |
| `ConnectionString` | `string` | -- | Connection string for the selected transport. Not required for `InMemory`. |
| `FullyQualifiedNamespace` | `string` | -- | Azure Service Bus namespace (e.g. `myns.servicebus.windows.net`); required when `Credential` is set instead of a connection string. |
| `Credential` | `TokenCredential` | -- | Azure credential for managed-identity authentication. Set in the callback (cannot be bound from configuration). |
| `Assemblies` | `List<Assembly>` | Empty | Assemblies to scan for `IIntegrationEventHandler<T>` implementations. |
| `EndpointName` | `string` | Sanitized entry assembly name | Queue (RabbitMQ) / subscription (Azure Service Bus) identity of this host. Replicas sharing it compete for messages. |
| `PrefetchCount` | `int` | `10` | Messages the broker delivers ahead of acknowledgement. Valid range: 1–1000. |
| `AutoProvision` | `bool` | `true` | Whether the transport declares exchanges/queues/topics/subscriptions itself. Set `false` with pre-created entities for least privilege. |
| `OutboxPollInterval` | `TimeSpan` | `5 seconds` | How often the `OutboxProcessor` polls for pending outbox messages. Minimum: 1 second. |
| `OutboxBatchSize` | `int` | `100` | Maximum number of outbox messages to process per polling cycle. Valid range: 1–1000. |
| `RetryPolicy` | `RetryPolicyOptions` | 5 attempts | Outbox dispatch retry: how many times the `OutboxProcessor` re-publishes a message before dead-lettering it. |
| `ConsumerRetry` | `RetryPolicyOptions` | 5 attempts | In-process consumer retry: how many times a handler is re-executed on failure before the message is dead-lettered on the transport. Independent of `RetryPolicy`. |

Both retry options are `RetryPolicyOptions` with `MaxAttempts` (≥ 1), `InitialInterval`, `MaxInterval`, and `IntervalIncrement` (`MaxInterval` ≥ `InitialInterval`). Bind them under `Messaging:RetryPolicy:*` and `Messaging:ConsumerRetry:*`.

::: info Handler auto-discovery
`AddModulusMessaging` scans the provided assemblies for all `IIntegrationEventHandler<TEvent>` implementations, registers them as scoped services, and subscribes the transport to each handled event type. No manual consumer registration is needed. When a message arrives, **all** registered handlers for that event type are invoked.
:::

## How It Works

At a high level, the messaging system works as follows:

```mermaid
flowchart LR
    A[Module A] -->|IMessageBus.Publish| B[Transport layer]
    B -->|Publish| C[Broker]
    C -->|Deliver| D[ConsumerDispatcher]
    D -->|Invoke| E[IIntegrationEventHandler]
    E -->|Handle| F[Module B]
```

1. **Module A** publishes an integration event via `IMessageBus`.
2. The **transport layer** serializes the event (System.Text.Json) and publishes it to the configured broker.
3. The **broker** (RabbitMQ, Azure Service Bus, or in-memory) routes the message to subscribed endpoints.
4. The **ConsumerDispatcher** deserializes the message, applies inbox idempotency (when an inbox store is registered) and in-process retry, then invokes every registered handler.
5. Your **IIntegrationEventHandler** receives and processes the event.

## What's Next

Dive into the specific concepts:

- **[Integration Events](./integration-events)** -- Define events and handlers for cross-module communication
- **[Message Bus](./message-bus)** -- The `IMessageBus` API for publishing and sending messages
- **[Transports](./transports)** -- Configure InMemory, RabbitMQ, or Azure Service Bus
- **[Outbox Pattern](./outbox-pattern)** -- Reliable message publishing with transactional outbox
- **[Inbox Pattern](./inbox-pattern)** -- Idempotent message consumption with the inbox pattern
- **[Migrating from MassTransit](./migrating-from-masstransit)** -- Upgrading from the MassTransit-based versions
