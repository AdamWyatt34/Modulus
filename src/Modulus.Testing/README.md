# Modulus.Testing

Test harness, in-memory test transport, and outbox/inbox assertion helpers for
[`ModulusKit.Messaging`](https://www.nuget.org/packages/ModulusKit.Messaging) — module-level
integration testing without hand-rolling a fake transport or querying the outbox tables by hand.

## Installation

```bash
dotnet add package ModulusKit.Testing
```

## Setup

Wire messaging exactly as you would in production — `AddModulusMessaging` with `Transport.InMemory`
— then swap the transport for the test-observable one with a single extra call:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging;
using Modulus.Testing;

var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<OutboxDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies.Add(typeof(OrderPlacedEvent).Assembly);
});
services.AddModulusTestTransport(); // must come after AddModulusMessaging
```

`AddModulusTestTransport()` replaces the registered `IMessageTransport` singleton with a
`TestMessageTransport` and registers the same instance under its concrete type, so it is
resolvable either way. It throws `InvalidOperationException` if `AddModulusMessaging` was not
called first — there is nothing to replace.

## Running the Pipeline: `ModulusMessagingTestHarness`

`AddModulusMessaging` registers hosted services (the transport consumer host, the outbox
processor) that a real `IHost` starts and stops for you. `ModulusMessagingTestHarness` does the
same for a test — in registration order on start, reverse order on stop, matching production
exactly (the consumer host subscribes before the outbox processor's first dispatch pass; the
outbox processor stops before consumers drain in-flight work):

```csharp
await using var harness = await ModulusMessagingTestHarness.StartAsync(services);

using var scope = harness.Provider.CreateScope();
var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

await messageBus.Publish(new OrderPlacedEvent(orderId, customerId, total));

await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);
```

`harness.Transport` resolves the `TestMessageTransport` swapped in by `AddModulusTestTransport()`
directly, without an extra `GetRequiredService` call.

## `TestMessageTransport`

Built entirely against the public transport SPI (`IMessageTransport`, `TransportEnvelope`,
`TransportSubscription`, `MessageDispatchResult`) — no internals access to `Modulus.Messaging` is
required or used. It mirrors the channel-per-event-type delivery semantics of the library's
internal in-memory transport, including `TransportEnvelope.ScheduledEnqueueTimeUtc` timer delivery
and `MessageDispatchResult.Retry` redelivery (broker-native retry mode) with an incremented
`modulus-delivery-attempt` header — so a test written against `ConsumerRetryMode.Broker` behaves
the same as it would against a real broker.

On top of that parity, it adds the observability a test actually needs:

- **`Published`** — every envelope passed to `PublishAsync`, in order, whether or not anything
  subscribes to it (a snapshot list; safe to enumerate from a concurrent test).
- **`DeadLettered`** — envelopes the consumer pipeline dead-lettered. The production in-memory
  transport only logs and drops these; the test transport keeps them so a test can assert a
  poison message actually reached dead-letter status.
- **`PublishFailure`** — set an `Exception` and every subsequent `PublishAsync` call throws it,
  for testing a caller's failure handling.
- **`PublishedEventsOf<TEvent>()` / `DeadLetteredEventsOf<TEvent>()`** — deserializes the matching
  envelope bodies back into `TEvent` with `System.Text.Json`, so assertions read typed events
  instead of raw envelopes.

```csharp
var published = harness.Transport.PublishedEventsOf<OrderPlacedEvent>();
published.ShouldHaveSingleItem().OrderId.ShouldBe(orderId);

var deadLettered = harness.Transport.DeadLetteredEventsOf<OrderPlacedEvent>();
```

## `TestWait`

Polls a condition instead of sleeping a fixed interval, so a test passes the instant the
condition holds and fails with a clear message when it never does:

```csharp
await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);
await TestWait.WaitForConditionAsync(
    async () => await dbContext.OrderReadModels.AnyAsync(o => o.Id == orderId),
    timeout: TimeSpan.FromSeconds(10),
    because: "the projection handler should have upserted the read model");
```

## Outbox and Inbox Query Helpers

`OutboxTestQueries` and `InboxTestQueries` are extension methods on `IServiceProvider` that
resolve `OutboxDbContext` / `InboxDbContext` from a fresh scope, so tests assert against the
tables the same way the CLI's `outbox`/`inbox` commands do, without writing that scope-and-query
boilerplate by hand:

```csharp
using Modulus.Testing;

var pending = await harness.Provider.GetPendingOutboxMessagesAsync();
var deadLettered = await harness.Provider.GetDeadLetteredOutboxMessagesAsync(maxAttempts: 5);
await harness.Provider.WaitForOutboxDrainAsync(TimeSpan.FromSeconds(10));

var processed = await harness.Provider.HasHandlerProcessedAsync(
    eventId, typeof(OrderPlacedEventHandler).FullName!);
```

> [!NOTE]
> **Inbox reservation tests need SQLite, not the EF Core in-memory provider.** `IInboxStore`'s
> `TryReserve`/takeover contract depends on the `InboxMessageConsumers` composite primary key
> actually being enforced and on `ExecuteUpdateAsync` semantics that the in-memory provider does
> not implement. `GetInboxMessagesAsync` and `HasHandlerProcessedAsync` work fine against
> either provider for read-only assertions, but back `InboxDbContext` with
> `UseSqlite("DataSource=:memory:")` (with the connection kept open for the test's lifetime) for
> any test that exercises reservation, takeover, or release behavior. The outbox has no such
> requirement — the EF Core in-memory provider is fine for `OutboxTestQueries`.

## Learn More

See the [Modulus documentation](https://adamwyatt34.github.io/Modulus/testing/modulus-testing) for
the full testing reference, including a walkthrough of the harness against a scaffolded module.
