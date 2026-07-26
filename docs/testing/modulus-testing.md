# ModulusKit.Testing

`ModulusKit.Testing` is a companion package for `ModulusKit.Messaging`: a test harness, a public
in-memory test transport, and outbox/inbox assertion helpers, so module-level integration testing
of your messaging pipeline doesn't mean hand-rolling a fake `IMessageTransport` or writing raw
`OutboxDbContext`/`InboxDbContext` queries every time.

## Installation

```bash
dotnet add package ModulusKit.Testing
```

Modules scaffolded with `modulus add-module` (and the solution-level `Tests.Integration` project
from `modulus init`) already reference it.

## Why not just fake `IMessageTransport` yourself?

The messaging transport SPI (`IMessageTransport`, `TransportEnvelope`, `TransportSubscription`,
`MessageDispatchResult`) is entirely public, so nothing stops you from writing your own fake. The
catch is semantics: the shipped in-memory transport does non-trivial work -- channel-per-event-type
routing, `ScheduledEnqueueTimeUtc` timer delivery, and `MessageDispatchResult.Retry` redelivery
with an incremented `modulus-delivery-attempt` header for `ConsumerRetryMode.Broker`. A hand-rolled
fake that just records published envelopes and calls a captured callback synchronously won't
exercise the same pipeline your production configuration does. `TestMessageTransport` mirrors that
production behavior exactly (it is built entirely against the public SPI, not shared internals)
and adds the introspection a test actually wants on top.

## Setup

Wire messaging exactly as you would in production -- `AddModulusMessaging` -- then swap the
transport for the test-observable one with one extra call:

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

`AddModulusTestTransport()` replaces the `IMessageTransport` singleton `AddModulusMessaging`
registered with a `TestMessageTransport`, and also registers the same instance under its concrete
type. Calling it before `AddModulusMessaging` (or on a collection that never called it) throws
`InvalidOperationException` -- there is nothing to replace yet.

## Running the pipeline: `ModulusMessagingTestHarness`

`AddModulusMessaging` registers hosted services -- the transport consumer host and the outbox
processor -- that a real `IHost` starts and stops for you automatically. `ModulusMessagingTestHarness`
does the same for a test: it builds the service provider, starts every registered `IHostedService`
in registration order, and stops them in reverse order on dispose. That ordering matters: the
consumer host subscribes *before* the outbox processor's first dispatch pass (a message published
with no subscriber is dropped), and on shutdown the outbox processor stops *before* consumers drain
their in-flight work.

```csharp
await using var harness = await ModulusMessagingTestHarness.StartAsync(services);

using var scope = harness.Provider.CreateScope();
var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

await messageBus.Publish(new OrderPlacedEvent(orderId, customerId, total));

await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);
```

`harness.Transport` resolves the `TestMessageTransport` registered by `AddModulusTestTransport()`
directly, without an extra `GetRequiredService` call.

## `TestMessageTransport`

On top of matching the shipped in-memory transport's delivery semantics, `TestMessageTransport`
exposes what a test actually needs to assert on:

| Member | What it gives you |
|---|---|
| `Published` | Every envelope passed to `PublishAsync`, in order -- a thread-safe snapshot list. |
| `DeadLettered` | Envelopes the consumer pipeline dead-lettered. The shipped in-memory transport only logs and drops these. |
| `PublishFailure` | Set an `Exception` and every subsequent publish throws it -- for testing a caller's failure handling. |
| `PublishedEventsOf<TEvent>()` / `DeadLetteredEventsOf<TEvent>()` | Deserializes matching envelope bodies back into `TEvent` with `System.Text.Json`. |

```csharp
harness.Transport.PublishedEventsOf<OrderPlacedEvent>()
    .ShouldHaveSingleItem().OrderId.ShouldBe(orderId);

harness.Transport.PublishFailure = new InvalidOperationException("simulated broker outage");
await Should.ThrowAsync<InvalidOperationException>(() => messageBus.Publish(anotherEvent));
```

## `TestWait`

The same polling helper the library's own test suite uses to await asynchronous delivery, instead
of a fixed `Task.Delay`:

```csharp
await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);

// Or, for a condition that itself needs to await something:
await TestWait.WaitForConditionAsync(
    async () => await dbContext.OrderReadModels.AnyAsync(o => o.Id == orderId),
    timeout: TimeSpan.FromSeconds(10),
    because: "the projection handler should have upserted the read model");
```

Both overloads default to a 5-second timeout, polling every 25ms, and throw `TimeoutException` (with
the optional `because` text) if the condition never holds.

## Outbox and inbox query helpers

`OutboxTestQueries` and `InboxTestQueries` are extension methods on `IServiceProvider` that resolve
`OutboxDbContext` / `InboxDbContext` from a fresh scope, replacing the raw query you'd otherwise
write by hand:

```csharp
using Modulus.Testing;

var pending = await harness.Provider.GetPendingOutboxMessagesAsync();
var deadLettered = await harness.Provider.GetDeadLetteredOutboxMessagesAsync(maxAttempts: 5);
await harness.Provider.WaitForOutboxDrainAsync(TimeSpan.FromSeconds(10));

var processed = await harness.Provider.HasHandlerProcessedAsync(
    eventId, typeof(OrderPlacedEventHandler).FullName!);
```

| Helper | Returns |
|---|---|
| `GetOutboxMessagesAsync()` | Every outbox row, oldest first. |
| `GetPendingOutboxMessagesAsync(maxAttempts: 5)` | Unprocessed rows below the attempt ceiling whose backoff/schedule has elapsed. |
| `GetDeadLetteredOutboxMessagesAsync(maxAttempts: 5)` | Unprocessed rows that reached the attempt ceiling. |
| `WaitForOutboxDrainAsync(timeout)` | Polls until no pending rows remain (via `TestWait`). |
| `GetInboxMessagesAsync()` | Every inbox row, oldest first. |
| `HasHandlerProcessedAsync(eventId, handlerFullName)` | Whether a specific handler has *completed* an event (a live reservation does not count). |

::: warning Inbox reservation tests need SQLite, not the EF Core in-memory provider
`GetInboxMessagesAsync` and `HasHandlerProcessedAsync` are read-only and work against either EF
Core provider. But if a test exercises `IInboxStore`'s reservation contract directly (`TryReserve`,
stale-reservation takeover, `ReleaseReservation`), back `InboxDbContext` with
`UseSqlite("DataSource=:memory:")` instead -- that contract depends on the
`InboxMessageConsumers` composite primary key actually being enforced and on `ExecuteUpdateAsync`
semantics the in-memory provider does not implement. Keep the `SqliteConnection` open for the
scope of the test, since an in-memory SQLite database is dropped when its last connection closes.
The outbox has no such requirement -- the EF Core in-memory provider is fine everywhere in
`OutboxTestQueries`.
:::

::: tip AddDbContext options run per scope
When seeding data in one scope and reading it back through these helpers (which each open their
own scope), capture the EF Core in-memory database name -- and, if you use one, the
`InMemoryDatabaseRoot` -- *outside* the `AddDbContext` options delegate. That delegate's default
lifetime is `Scoped`, so a delegate that calls `Guid.NewGuid()` inline generates a fresh,
disjoint database on every scope instead of sharing one across your test.
:::

## Learn More

See the [Messaging](/messaging/) documentation for the outbox/inbox pattern, transports, and
distributed tracing this package tests against, and [Integration Testing](./integration-testing)
for how it fits alongside `WebApplicationFactory`-based endpoint tests.
