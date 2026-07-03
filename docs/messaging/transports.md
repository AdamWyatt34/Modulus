# Transports

Modulus messaging runs on an in-house transport layer with three providers: **InMemory**, **RabbitMQ**, and **Azure Service Bus**. The in-memory transport ships inside the core `ModulusKit.Messaging` package; the broker transports ship as separate packages so your host only pulls in the broker client it actually uses. Your handler code remains identical regardless of which transport you choose -- only configuration and one package reference change.

## Transport Packages

| Transport | Package | Broker client | Registration |
|---|---|---|---|
| `InMemory` | `ModulusKit.Messaging` (built-in) | None | Nothing extra |
| `RabbitMq` | `ModulusKit.Messaging.RabbitMq` | RabbitMQ.Client 7.2.1 | `services.AddModulusRabbitMqTransport()` |
| `AzureServiceBus` | `ModulusKit.Messaging.AzureServiceBus` | Azure.Messaging.ServiceBus 7.20.1 | `services.AddModulusAzureServiceBusTransport()` |

A broker transport is activated in two steps: register the transport factory with its one-line extension, and select it via `MessagingOptions.Transport`. Registration order relative to `AddModulusMessaging` does not matter.

```csharp
public enum Transport
{
    InMemory,
    RabbitMq,
    AzureServiceBus
}
```

::: warning Transport selected but not registered
If configuration selects `RabbitMq` or `AzureServiceBus` but the matching `AddModulus*Transport()` call is missing, the host fails at startup with an exception telling you which package to install and which extension method to call. Nothing silently falls back to in-memory.
:::

## Configuration

The recommended setup binds the `Messaging` section from `appsettings.json` -- this is the section `modulus init --transport` scaffolds:

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

The RabbitMQ connection string format is `amqp://user:pass@host:5672/vhost` (for local development against the Docker default broker: `amqp://guest:guest@localhost:5672`). Keep real credentials out of `appsettings.json` -- supply them via user secrets or environment variables, e.g. `Messaging__ConnectionString`.

```csharp
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

Switching brokers is a configuration-only change as long as the transport package is registered: flip `Transport` to `"InMemory"`, `"RabbitMq"`, or `"AzureServiceBus"` and supply the matching connection settings. You can register both broker transports side by side and let configuration decide per environment.

Everything can also be configured imperatively:

:::code-group

```csharp [InMemory]
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies.Add(typeof(Program).Assembly);
    // No ConnectionString required
});
```

```csharp [RabbitMQ]
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

```csharp [Azure Service Bus]
builder.Services.AddModulusAzureServiceBusTransport();
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.AzureServiceBus;
    options.ConnectionString = builder.Configuration.GetConnectionString("AzureServiceBus");
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

:::

::: tip No handler changes required
Switching transports never touches your code. Your `IIntegrationEventHandler<T>` implementations, `IMessageBus` calls, and event definitions remain exactly the same. Use InMemory for development and a real broker in production.
:::

## Transport Options

Three options shape how the broker transports behave. All bind from the `Messaging` configuration section.

| Property | Default | Description |
|---|---|---|
| `EndpointName` | Sanitized entry assembly name | The endpoint identity of this host: the RabbitMQ queue name and the Azure Service Bus subscription name its consumers receive on. **Replicas sharing an endpoint name compete for messages** (each event is processed by one replica). Distinct services that should each receive every event must use distinct endpoint names. |
| `PrefetchCount` | `10` | How many messages the broker delivers ahead of acknowledgement (RabbitMQ prefetch / Azure Service Bus concurrent calls and prefetch). Valid range: 1–1000. |
| `AutoProvision` | `true` | Whether the transport declares its own topology (exchanges, queues, topics, subscriptions) at startup and on first publish. Set to `false` for least-privilege deployments where entities are pre-created. |

::: info AutoProvision permissions
With `AutoProvision = true` (the default), the credential needs **Manage** rights on Azure Service Bus and **declare** permissions on RabbitMQ. For least-privilege production deployments, pre-create the topology using the naming conventions below and set `AutoProvision` to `false` -- the transport then only needs send/receive rights.
:::

## Serialization and Wire Format

All transports use the same wire format:

- The message body is the **bare event JSON**, serialized with `System.Text.Json` default options.
- Metadata rides in native transport properties: `MessageId` = the event's `EventId`, `CorrelationId`, the message type/subject = the full type name, and a `modulus-occurred-on` header (RabbitMQ) / application property (Azure Service Bus) carrying the `OccurredOn` timestamp.

This format is intentionally plain: any client that can read JSON and the native broker properties can interoperate. It is **not** compatible with the MassTransit envelope format used by earlier versions -- see [Migrating from MassTransit](./migrating-from-masstransit) if you are upgrading an existing deployment.

## InMemory Transport

The InMemory transport is built on `System.Threading.Channels` and runs entirely within the application process, with one channel per event type.

**When to use:**
- Local development without requiring Docker or a broker installation
- Unit and integration testing (no fixed delays needed -- delivery is immediate)
- Prototyping and rapid iteration
- Single-process deployments where cross-service communication is not needed

**Semantics:**
- No external dependencies and no `ConnectionString` required
- Publishing an event with no subscriber **drops the message** -- the same behavior as a fanout exchange with no bindings
- Dead-lettering is **log + drop**: when consumer retries are exhausted the failure is logged and the message is discarded (there is no DLQ)
- Messages are not persisted -- if the process restarts, unprocessed messages are lost

```csharp
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

::: warning Not for production
The InMemory transport does not provide durable message delivery. Use it for development and testing only. For production workloads, use RabbitMQ or Azure Service Bus.
:::

## RabbitMQ Transport

The RabbitMQ transport (`ModulusKit.Messaging.RabbitMq`) connects to a [RabbitMQ](https://www.rabbitmq.com/) broker over AMQP using `RabbitMQ.Client` 7.2.1.

```csharp
builder.Services.AddModulusRabbitMqTransport();
```

### Topology

| Entity | Name | Notes |
|---|---|---|
| Exchange (per event type) | Lower-cased full type name, e.g. `myapp.orders.integration.ordercreatedevent` | Durable **fanout** exchange |
| Queue (per endpoint) | `{EndpointName}` | Durable; bound to every exchange the endpoint subscribes to; replicas sharing the name compete |
| Dead-letter exchange | `{EndpointName}.dlx` | Targeted via the queue's `x-dead-letter-exchange` argument |
| Dead-letter queue | `{EndpointName}.dead-letter` | Bound to the dead-letter exchange |
| Send queue (point-to-point) | Command type name | `Send` publishes via the **default exchange** with the queue name as routing key |

When consumer retries are exhausted, the message is rejected without requeue and RabbitMQ routes it through `{EndpointName}.dlx` into `{EndpointName}.dead-letter`.

### Reliability

- **Publisher confirmations are enabled.** A publish only completes when the broker confirms it. If a confirm fails, the publish throws -- the outbox marks the message as failed and retries it on a later poll, so nothing is silently lost.
- **Automatic connection recovery.** The client reconnects and restores its topology after broker restarts and network blips.

**Connection string format:**

```
amqp://username:password@hostname:port/vhost
```

```csharp
// From configuration (recommended -- keep credentials in user secrets or environment variables)
options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
```

For local development against the Docker image below, the connection string is `amqp://guest:guest@localhost:5672`; with a virtual host, append it: `amqp://myuser:mypass@rabbitmq.internal:5672/myapp`.

::: tip Running RabbitMQ locally
The quickest way to run RabbitMQ locally is with Docker:

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3-management
```

The management UI is available at `http://localhost:15672` (default credentials: `guest` / `guest`).
:::

## Azure Service Bus Transport

The Azure Service Bus transport (`ModulusKit.Messaging.AzureServiceBus`) connects to [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/) using `Azure.Messaging.ServiceBus` 7.20.1.

```csharp
builder.Services.AddModulusAzureServiceBusTransport();
```

::: warning Standard or Premium tier required
The topology is built on topics and subscriptions, which the **Basic** tier does not support. Use a Standard or Premium namespace.
:::

### Topology

| Entity | Name | Notes |
|---|---|---|
| Topic (per event type) | Lower-cased full type name, e.g. `myapp.orders.integration.ordercreatedevent` | One topic per published event type |
| Subscription (per endpoint) | `{EndpointName}` | Names longer than the 50-character service limit are truncated with a stable 8-character hash suffix so distinct endpoints never collide |
| Dead-letter queue | Subscription's built-in DLQ | Dead-lettered with reason `RetriesExhausted` after consumer retries run out |
| Send queue (point-to-point) | Command type name | `Send` delivers to a queue named after the command type |

### Consumption Model

- Each subscription is consumed with a `ServiceBusProcessor` with **auto-complete off** -- messages are completed only after the consumer pipeline succeeds, and dead-lettered when it gives up.
- `MaxConcurrentCalls` is set to `PrefetchCount`.
- Message **lock auto-renewal is capped at 5 minutes**. The worst-case sum of your `ConsumerRetry` delays (plus handler execution time) must stay below that cap, or the lock expires mid-retry and the message is redelivered. Tune `ConsumerRetry:MaxAttempts` / `ConsumerRetry:MaxInterval` accordingly.

**Connection string format:**

```
Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key
```

```csharp
// From configuration (recommended -- keep the key in user secrets or Key Vault)
options.ConnectionString = builder.Configuration.GetConnectionString("AzureServiceBus");
```

### Managed Identity

Instead of a connection string, supply `FullyQualifiedNamespace` plus a `TokenCredential` in the configuration callback:

```json
{
  "Messaging": {
    "Transport": "AzureServiceBus",
    "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net"
  }
}
```

```csharp
builder.Services.AddModulusAzureServiceBusTransport();
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Credential = new DefaultAzureCredential();
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

When `Credential` is set, `ConnectionString` is ignored and `FullyQualifiedNamespace` is required.

## Environment-Based Transport Selection

Because the transport is chosen by the `Messaging` section's `Transport` value, per-environment selection is just per-environment configuration:

```json
// appsettings.Development.json
{ "Messaging": { "Transport": "InMemory" } }
```

```json
// appsettings.Production.json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "EndpointName": "orders-service"
  }
}
```

```csharp
// Register the broker transport unconditionally -- it is inert unless selected.
builder.Services.AddModulusRabbitMqTransport();

builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(CatalogModule).Assembly);
    options.Assemblies.Add(typeof(OrdersModule).Assembly);
});
```

`EndpointName`, `PrefetchCount`, `AutoProvision`, `OutboxBatchSize`, `OutboxPollInterval`, and the `RetryPolicy` (outbox dispatch) and `ConsumerRetry` (consumer pipeline) sub-sections all bind the same way. The callback runs after binding, so it can also override any bound value or supply an Azure `TokenCredential`.

## Transport Comparison

| Feature | InMemory | RabbitMQ | Azure Service Bus |
|---|---|---|---|
| **Package** | `ModulusKit.Messaging` | `ModulusKit.Messaging.RabbitMq` | `ModulusKit.Messaging.AzureServiceBus` |
| **External broker** | No | Yes (self-hosted) | Yes (managed, Standard+ tier) |
| **Message durability** | No | Yes (durable entities, publisher confirms) | Yes |
| **Dead-letter support** | No (log + drop) | Yes (`{EndpointName}.dead-letter`) | Yes (built-in subscription DLQ) |
| **Pub/sub topology** | Channel per event type | Fanout exchange per event type | Topic per event type |
| **Endpoint identity** | n/a | Queue `{EndpointName}` | Subscription `{EndpointName}` |
| **Cost** | Free | Free (open-source) | Pay-per-use |
| **Best for** | Dev / Test | Self-hosted / On-prem | Azure workloads |
| **Connection string** | Not required | `amqp://...` | `Endpoint=sb://...` or managed identity |

## See Also

- [Overview](./index) -- Messaging setup and `MessagingOptions` reference
- [Migrating from MassTransit](./migrating-from-masstransit) -- Upgrading from the MassTransit-based versions
- [Message Bus](./message-bus) -- The `IMessageBus` API
- [Outbox Pattern](./outbox-pattern) -- Reliable publishing regardless of transport
- [Inbox Pattern](./inbox-pattern) -- Idempotent consumption
