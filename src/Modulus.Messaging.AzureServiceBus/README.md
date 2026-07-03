# ModulusKit.Messaging.AzureServiceBus

Azure Service Bus transport for [ModulusKit.Messaging](https://www.nuget.org/packages/ModulusKit.Messaging), built directly on the MIT-licensed `Azure.Messaging.ServiceBus` SDK — no MassTransit dependency.

> **Requires Standard or Premium tier.** The topology uses topics, which the Basic tier does not support.

## Usage

```csharp
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(Program).Assembly);
    // For managed identity instead of a connection string:
    // options.Credential = new DefaultAzureCredential();
});
builder.Services.AddModulusAzureServiceBusTransport();
```

```json
{
  "Messaging": {
    "Transport": "AzureServiceBus",
    "FullyQualifiedNamespace": "myns.servicebus.windows.net",
    "EndpointName": "my-service"
  }
}
```

## Topology

| Entity | Name | Purpose |
|---|---|---|
| Topic | `<event type full name, lower-cased>` | One per event type; publish target |
| Subscription | `<EndpointName>` (50-char cap with stable hash suffix) | One per endpoint; replicas compete |
| Dead-letter | built-in subscription DLQ | Messages that exhausted `ConsumerRetry` (reason `RetriesExhausted`) |

Lock auto-renewal is capped at 5 minutes — keep the worst-case sum of `ConsumerRetry` delays below that. `AutoProvision` (default `true`) creates topics/subscriptions at startup and requires Manage rights; pre-create entities and set it to `false` for least-privilege deployments.

## Testing

`AzureServiceBusTopology` (naming) and `AzureServiceBusEnvelopeMapper` (envelope/property mapping) are pure and unit-tested in `Modulus.Messaging.Tests/AzureServiceBus`. `tests/Modulus.Messaging.AzureServiceBus.IntegrationTests` runs the transport against the official `mcr.microsoft.com/azure-messaging/servicebus-emulator` (with its required `mssql/server` companion, both managed by the `Testcontainers.ServiceBus` module), covering publish/consume roundtrip and dead-lettering after retry exhaustion (asserted via a `ServiceBusReceiver` on the subscription's DLQ `SubQueue`).

The emulator does not support `ServiceBusAdministrationClient`, so every scenario runs with `MessagingOptions.AutoProvision = false` against topology pre-declared in the project's checked-in `Config.json`. A same-project, non-`Category=Integration` `[Fact]` (`ConfigJsonDriftGuardTests`) asserts the topic/subscription names in `Config.json` exactly match what `AzureServiceBusTopology.TopicName`/`SubscriptionName` compute for the fixture event types and endpoint names the Integration tests use, so a rename or a topology-naming change fails fast without needing Docker.

## License

MIT — part of the [Modulus](https://github.com/adamwyatt34/Modulus) project.
