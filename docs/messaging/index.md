# Messaging Overview

Modulus provides a messaging abstraction over [MassTransit](https://masstransit.io/) for cross-module communication via integration events. It supports InMemory, RabbitMQ, and Azure Service Bus transports, and includes built-in transactional outbox and inbox patterns for reliable, exactly-once message delivery.

## Why Abstract over MassTransit?

| Concern | Modulus Messaging |
|---|---|
| **Swap transports freely** | Switch between InMemory, RabbitMQ, and Azure Service Bus by changing a single enum value. No handler code changes required. |
| **Cleaner handler interface** | Implement `IIntegrationEventHandler<TEvent>` instead of MassTransit's `IConsumer<ConsumeContext<T>>`. Less ceremony, more focus on business logic. |
| **Automatic idempotency** | Every handler is transparently wrapped with `IdempotentConsumerAdapter` to prevent duplicate processing when the inbox store is registered. |
| **Transactional outbox built-in** | Save domain state and outbox messages in the same database transaction. A background processor publishes them reliably to the broker. |
| **Convention-based discovery** | Handlers are auto-discovered from the assemblies you specify -- no manual MassTransit consumer registration. |

## Installation

If you scaffolded your solution with the Modulus CLI, the messaging package is already referenced. To add it manually:

```bash
dotnet add package ModulusKit.Messaging
```

## Quick Setup

Register messaging in your host project's `Program.cs` or composition root:

```csharp
using Modulus.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://guest:guest@localhost:5672";
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

You can add multiple assemblies to scan for handlers across all your modules:

```csharp
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://guest:guest@localhost:5672";
    options.Assemblies.Add(typeof(CatalogModule).Assembly);
    options.Assemblies.Add(typeof(OrdersModule).Assembly);
    options.Assemblies.Add(typeof(PaymentModule).Assembly);
});
```

## MessagingOptions Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Transport` | `Transport` | -- | The message broker transport to use. One of `InMemory`, `RabbitMq`, or `AzureServiceBus`. |
| `ConnectionString` | `string` | -- | Connection string for the selected transport. Not required for `InMemory`. |
| `Assemblies` | `List<Assembly>` | Empty | Assemblies to scan for `IIntegrationEventHandler<T>` implementations. |
| `OutboxPollInterval` | `TimeSpan` | `5 seconds` | How often the `OutboxProcessor` polls for pending outbox messages. |
| `OutboxBatchSize` | `int` | `100` | Maximum number of outbox messages to process per polling cycle. |

::: info Handler auto-discovery
`AddModulusMessaging` scans the provided assemblies for all `IIntegrationEventHandler<TEvent>` implementations. Each handler is wrapped with `IdempotentConsumerAdapter<TEvent>` and registered as a MassTransit consumer automatically. No manual consumer registration is needed.
:::

## How It Works

At a high level, the messaging system works as follows:

```mermaid
flowchart LR
    A[Module A] -->|IMessageBus.Publish| B[MassTransit]
    B -->|Transport| C[Broker]
    C -->|Deliver| D[IdempotentConsumerAdapter]
    D -->|Invoke| E[IIntegrationEventHandler]
    E -->|Handle| F[Module B]
```

1. **Module A** publishes an integration event via `IMessageBus`.
2. **MassTransit** serializes and sends the event to the configured broker.
3. The **broker** (RabbitMQ, Azure Service Bus, or in-memory) routes the message to subscribers.
4. **IdempotentConsumerAdapter** ensures the event has not already been processed (when an inbox store is registered).
5. Your **IIntegrationEventHandler** receives and processes the event.

## What's Next

Dive into the specific concepts:

- **[Integration Events](./integration-events)** -- Define events and handlers for cross-module communication
- **[Message Bus](./message-bus)** -- The `IMessageBus` API for publishing and sending messages
- **[Transports](./transports)** -- Configure InMemory, RabbitMQ, or Azure Service Bus
- **[Outbox Pattern](./outbox-pattern)** -- Reliable message publishing with transactional outbox
- **[Inbox Pattern](./inbox-pattern)** -- Idempotent message consumption with the inbox pattern
