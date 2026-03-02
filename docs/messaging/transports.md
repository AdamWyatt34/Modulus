# Transports

Modulus supports three message broker transports: **InMemory**, **RabbitMQ**, and **Azure Service Bus**. You select a transport by setting the `Transport` enum on `MessagingOptions`. Your handler code remains identical regardless of which transport you choose -- only the configuration changes.

## Transport Enum

```csharp
public enum Transport
{
    InMemory,
    RabbitMq,
    AzureServiceBus
}
```

## Configuration

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
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://guest:guest@localhost:5672";
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

```csharp [Azure Service Bus]
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.AzureServiceBus;
    options.ConnectionString = "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key";
    options.Assemblies.Add(typeof(Program).Assembly);
});
```

:::

::: tip No handler changes required
Switching transports is a configuration-only change. Your `IIntegrationEventHandler<T>` implementations, `IMessageBus` calls, and event definitions remain exactly the same. This makes it straightforward to use InMemory for development and a real broker in production.
:::

## InMemory Transport

The InMemory transport runs entirely within the application process. Messages are delivered directly between publishers and consumers without any external broker.

**When to use:**
- Local development without requiring Docker or a broker installation
- Unit and integration testing
- Prototyping and rapid iteration
- Single-process deployments where cross-service communication is not needed

**Characteristics:**
- No external dependencies -- no broker to install or configure
- No `ConnectionString` required
- Messages are not persisted -- if the process restarts, unprocessed messages are lost
- All delivery is in-process -- no network latency

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

The RabbitMQ transport connects to a [RabbitMQ](https://www.rabbitmq.com/) broker using the AMQP protocol. It provides durable queues, message acknowledgment, and exchange-based routing.

**When to use:**
- Self-hosted or on-premises environments
- Docker and Kubernetes deployments
- When you need fine-grained control over queues, exchanges, and routing
- Open-source and no per-message cost

**Characteristics:**
- Durable message delivery with acknowledgments
- Exchange and queue-based routing
- Dead-letter exchange support for failed messages
- MassTransit handles connection recovery automatically

**Connection string format:**

```
amqp://username:password@hostname:port/vhost
```

**Examples:**

```csharp
// Local development
options.ConnectionString = "amqp://guest:guest@localhost:5672";

// With virtual host
options.ConnectionString = "amqp://myuser:mypassword@rabbitmq.internal:5672/myapp";

// From configuration
options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
```

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

The Azure Service Bus transport connects to [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/) for fully managed, cloud-native messaging.

**When to use:**
- Azure-hosted workloads
- When you need a fully managed broker with no infrastructure to maintain
- Enterprise scenarios requiring advanced features (sessions, scheduled delivery, auto-forwarding)
- When SLA-backed message delivery is required

**Characteristics:**
- Fully managed -- no broker infrastructure to maintain
- Topics and subscriptions for pub/sub
- Queues for point-to-point messaging
- Built-in dead-letter queues
- SLA-backed availability

**Connection string format:**

```
Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key
```

**Examples:**

```csharp
// Direct connection string
options.ConnectionString = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123";

// From configuration
options.ConnectionString = builder.Configuration.GetConnectionString("AzureServiceBus");
```

## Environment-Based Transport Selection

A common pattern is to select the transport based on the environment:

```csharp
builder.Services.AddModulusMessaging(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.Transport = Transport.InMemory;
    }
    else
    {
        options.Transport = Transport.RabbitMq;
        options.ConnectionString = builder.Configuration
            .GetConnectionString("RabbitMq")!;
    }

    options.Assemblies.Add(typeof(CatalogModule).Assembly);
    options.Assemblies.Add(typeof(OrdersModule).Assembly);
});
```

::: info Configuration-driven transport
You can also read the transport from `appsettings.json`:

```json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "amqp://guest:guest@localhost:5672"
  }
}
```

```csharp
var messagingConfig = builder.Configuration.GetSection("Messaging");

builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Enum.Parse<Transport>(messagingConfig["Transport"]!);
    options.ConnectionString = messagingConfig["ConnectionString"];
    options.Assemblies.Add(typeof(Program).Assembly);
});
```
:::

## Transport Comparison

| Feature | InMemory | RabbitMQ | Azure Service Bus |
|---|---|---|---|
| **External broker** | No | Yes (self-hosted) | Yes (managed) |
| **Message durability** | No | Yes | Yes |
| **Dead-letter support** | No | Yes | Yes |
| **Network latency** | None | Low (local) / Variable | Variable |
| **Cost** | Free | Free (open-source) | Pay-per-use |
| **Best for** | Dev / Test | Self-hosted / On-prem | Azure workloads |
| **Connection string** | Not required | `amqp://...` | `Endpoint=sb://...` |

## See Also

- [Overview](./index) -- Messaging setup and `MessagingOptions` reference
- [Message Bus](./message-bus) -- The `IMessageBus` API
- [Outbox Pattern](./outbox-pattern) -- Reliable publishing regardless of transport
- [Inbox Pattern](./inbox-pattern) -- Idempotent consumption
