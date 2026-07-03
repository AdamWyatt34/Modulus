# ModulusKit.Messaging.RabbitMq

RabbitMQ transport for [ModulusKit.Messaging](https://www.nuget.org/packages/ModulusKit.Messaging), built directly on the OSS `RabbitMQ.Client` — no MassTransit dependency.

## Usage

```csharp
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
});
builder.Services.AddModulusRabbitMqTransport();
```

```json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "amqp://guest:guest@localhost:5672/",
    "EndpointName": "my-service"
  }
}
```

## Topology

| Entity | Name | Purpose |
|---|---|---|
| Fanout exchange | `<event type full name, lower-cased>` | One per event type; publish target |
| Queue | `<EndpointName>` | One per endpoint; bound to every subscribed exchange; replicas compete |
| Dead-letter exchange | `<EndpointName>.dlx` | Target of `x-dead-letter-exchange` |
| Dead-letter queue | `<EndpointName>.dead-letter` | Messages that exhausted `ConsumerRetry` |

Publisher confirmations are enabled: a failed confirm surfaces as an exception the outbox retries. Topology is declared automatically on startup and first publish; set `Messaging:AutoProvision` to `false` for least-privilege deployments with pre-created entities.

## License

MIT — part of the [Modulus](https://github.com/adamwyatt34/Modulus) project.
