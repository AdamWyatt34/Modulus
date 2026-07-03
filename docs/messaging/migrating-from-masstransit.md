# Migrating from MassTransit

Earlier versions of `ModulusKit.Messaging` were built on MassTransit 7.x. MassTransit's move to a commercial license made that dependency untenable for an MIT-licensed library, so the messaging stack now runs on an **in-house transport layer** built directly on the official broker clients (`RabbitMQ.Client` and `Azure.Messaging.ServiceBus`).

The public API you code against is unchanged -- `AddModulusMessaging`, `IMessageBus`, `IIntegrationEvent` / `IntegrationEvent`, `IIntegrationEventHandler<TEvent>`, and the outbox/inbox stores are all the same. What changed is everything underneath, and one of those changes (the wire format) requires a coordinated deployment.

## What Changed

### Packages

MassTransit and its transitive dependencies (including the `Newtonsoft.Json` pin, which existed only for MassTransit) are gone. Broker support is now split into transport packages:

| Package | Contains |
|---|---|
| `ModulusKit.Messaging` | Core messaging: `IMessageBus`, outbox/inbox, consumer pipeline, and the **in-memory transport** |
| `ModulusKit.Messaging.RabbitMq` | RabbitMQ transport (RabbitMQ.Client 7.2.1) |
| `ModulusKit.Messaging.AzureServiceBus` | Azure Service Bus transport (Azure.Messaging.ServiceBus 7.20.1) |

Broker transports need one new registration line alongside your existing `AddModulusMessaging` call (order does not matter):

```csharp
// RabbitMQ
builder.Services.AddModulusRabbitMqTransport();

// Azure Service Bus
builder.Services.AddModulusAzureServiceBusTransport();
```

Config-only transport switching still works via the `Messaging` section's `Transport` value (`"InMemory"` | `"RabbitMq"` | `"AzureServiceBus"`). If configuration selects a broker transport whose package is not registered, the host throws at startup with instructions for which package to install.

### Wire format -- not compatible

The new transports do **not** use the MassTransit envelope. The body on the wire is the bare event JSON (System.Text.Json default options); metadata rides in native transport properties (`MessageId` = `EventId`, `CorrelationId`, type/subject = full type name, plus a `modulus-occurred-on` header/application property).

An old consumer cannot read new messages, and a new consumer cannot read old ones. This is why the upgrade requires the [drain-before-upgrade procedure](#upgrade-procedure-drain-before-upgrade) below.

### Topology names

The broker entities are named differently, so the old and new topologies exist side by side until you clean up:

| Entity | MassTransit era | Now |
|---|---|---|
| RabbitMQ exchange | Per message type, MassTransit naming | Durable fanout exchange per event type, **lower-cased full type name** |
| RabbitMQ queue | Per consumer endpoint, MassTransit naming | One durable queue per endpoint: `{EndpointName}`, with DLX `{EndpointName}.dlx` and DLQ `{EndpointName}.dead-letter` |
| ASB topic | Per message type, MassTransit naming | Topic per event type, lower-cased full type name |
| ASB subscription | Per consumer endpoint | `{EndpointName}` (truncated to 50 chars with a stable 8-char hash when longer) |

`EndpointName` is a new `MessagingOptions` member; it defaults to the sanitized entry assembly name. Replicas sharing an endpoint name compete for messages. See [Transports](./transports) for the full topology reference.

### Consumer retry formula -- approximation, not identical

In-process consumer retry (`ConsumerRetry`) uses exponential backoff:

```
delay(n) = min(MaxInterval, InitialInterval + IntervalIncrement * (2^(n-1) - 1))
```

This **approximates but is not identical to** MassTransit's `Exponential` retry. If you tuned `ConsumerRetry` values around MassTransit's exact delays, re-check the resulting schedule -- especially on Azure Service Bus, where the worst-case sum of retry delays must stay below the 5-minute lock auto-renewal cap. `ConsumerRetry` and `RetryPolicy` (outbox dispatch) remain independent, and outbox dead-letter semantics are unchanged (`Attempts >= RetryPolicy.MaxAttempts` stops fetching; the `modulus outbox list-failed` / `retry` / `purge` CLI commands still apply).

### Behavior change: all handlers are now invoked

The MassTransit-era adapter had a bug-turned-behavior: when multiple `IIntegrationEventHandler<TEvent>` implementations were registered for the same event, **only the last-registered handler ran**. The new consumer pipeline (`ConsumerDispatcher`) resolves and invokes **all** registered handlers per event, each independently tracked by the inbox at the `(EventId, handlerName)` level.

If you have events with multiple handlers, audit them before upgrading: handlers that never ran before will start running.

### Send was removed

`IMessageBus.Send` no longer exists. Modulus never provided a consuming pipeline for point-to-point commands (the receiving side had to consume the queue itself), so the API implied wiring that MassTransit used to supply and Modulus does not. Migrate `Send` call sites to one of:

- an **integration event** (`Publish` + `IIntegrationEventHandler<T>` in the receiving module) when the receiver lives in the same solution, or
- direct broker access (e.g. RabbitMQ.Client / Azure SDK) when you genuinely need to enqueue into a queue owned by an external service.

### Other behavior notes

- Unknown message types are acknowledged and dropped with a warning; unreadable (non-deserializable) bodies are dead-lettered without retry.
- RabbitMQ publishes use publisher confirmations: a failed confirm surfaces as an exception, so the outbox marks the message failed and retries it.
- The in-memory transport drops events published with no subscriber and has no DLQ (dead-letter = log + drop).
- Messaging tests now run against the in-memory transport with no fixed delays; RabbitMQ has Testcontainers-based integration tests (`Category=Integration`).

## Upgrade Procedure: Drain Before Upgrade

Because the wire format and entity names both changed, old and new versions cannot exchange messages. Upgrade with a drain window:

1. **Stop publishers.** Stop (or scale to zero) every service that publishes integration events. The outbox pattern makes this safe: events raised while publishers are stopping are held in the outbox table and dispatched after the upgrade.
2. **Let consumers drain.** Leave the old consumers running until every queue/subscription is empty. Verify via the RabbitMQ management UI or the Azure portal that message counts are zero, including dead-letter queues you care about.
3. **Stop consumers.**
4. **Deploy publishers and consumers together** on the new version:
   - Add the transport package (`ModulusKit.Messaging.RabbitMq` or `ModulusKit.Messaging.AzureServiceBus`) and its `AddModulus*Transport()` call.
   - Set `EndpointName` explicitly for each consuming service (recommended -- do not rely on the assembly-name default across a fleet).
   - Review `PrefetchCount` (default 10) and `AutoProvision` (default `true`; needs Manage rights on ASB / declare permissions on RabbitMQ -- set `false` with pre-created entities for least privilege).
5. **Start everything.** With `AutoProvision` enabled, the new exchanges/queues (RabbitMQ) or topics/subscriptions (ASB) are declared on startup and first publish. Pending outbox messages flow out in the new format.
6. **Clean up the old topology.** Once the new deployment is verified, delete the old MassTransit exchanges, queues, topics, and subscriptions -- their names differ from the new conventions, so they are now dead weight. Anything still sitting in an old queue at this point is unreadable by the new consumers; if messages remain, you skipped the drain in step 2.

::: warning No rolling upgrade
Do not run old and new versions side by side against the same broker. New consumers will not receive old-format messages (different exchanges/topics), and any old consumer bound to a new entity would fail to deserialize the body.
:::

## Checklist

- [ ] Multiple handlers per event audited (they **all** run now)
- [ ] `ConsumerRetry` schedule re-checked against the new delay formula (and the 5-minute ASB lock cap)
- [ ] Transport package installed and `AddModulus*Transport()` called
- [ ] `EndpointName` set per service
- [ ] `AutoProvision` decision made (default `true`; `false` + pre-created entities for least privilege)
- [ ] Direct `MassTransit`/`Newtonsoft.Json` package references removed from your hosts (Modulus no longer pins them)
- [ ] Drain-before-upgrade window scheduled
- [ ] Old MassTransit topology deleted after verification

## See Also

- [Transports](./transports) -- Full topology, options, and configuration reference
- [Outbox Pattern](./outbox-pattern) -- Why stopping publishers is safe
- [Inbox Pattern](./inbox-pattern) -- Per-handler idempotency in the new consumer pipeline
