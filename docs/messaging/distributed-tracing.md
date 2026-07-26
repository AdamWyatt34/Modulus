# Distributed Tracing

Modulus messaging propagates **W3C trace context** (`traceparent`/`tracestate`) across the broker, so a request that publishes an event and the handlers that consume it — possibly seconds later, in another process — appear in one distributed trace.

## What gets emitted

| Span | ActivitySource | Kind | When |
|---|---|---|---|
| `{event} publish` | `Modulus.Messaging` | Producer | Direct `IMessageBus.Publish` |
| `outbox.dispatch` | `Modulus.Messaging.Outbox` | Producer | Outbox processor publishing a stored row |
| `{event} process` | `Modulus.Messaging` | Consumer | Transport delivery through the consumer pipeline |

Enable them in OpenTelemetry alongside the existing meter:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Modulus.Messaging")         // publish + consume spans
        .AddSource("Modulus.Messaging.Outbox")  // outbox dispatch spans
        .AddSource("Modulus.Mediator"))         // TracingBehavior, if enabled
    .WithMetrics(metrics => metrics.AddMeter("Modulus.Messaging"));
```

The source and header names are available as constants on `MessagingDiagnostics`.

## How context flows

**Direct publish** — `IMessageBus.Publish` starts a Producer span and injects its context into the envelope's `Headers`, which each transport maps to its native mechanism (RabbitMQ AMQP headers, Azure Service Bus application properties; the in-memory transport passes the envelope through unchanged).

**Through the outbox** — the save and the publish happen at different times, so the context is persisted and re-linked:

1. `IOutboxStore.Save` captures the *ambient* activity (your request's span) into the row's `TraceParent`/`TraceState` columns.
2. When the outbox processor dispatches the row, its `outbox.dispatch` Producer span **links** to that saved context (an `ActivityLink`, not a parent — the originating request finished long ago; links are the OTel shape for deferred producers).
3. The envelope carries the *dispatch* span's context, so consumer latency attributes to the dispatch while the originating request stays one link-hop away.

**Consume** — the consumer pipeline starts one Consumer span per delivery, parented on the context extracted from the envelope headers. It wraps the whole in-process retry loop, and `Activity.Current` flows into your handlers — mediator calls instrumented by `TracingBehavior` nest under it automatically. Tags: `modulus.message_id`, `modulus.message_type`, `modulus.outcome` (`acknowledge`/`dead_letter`), `modulus.attempt` on retries; dead-letters set the span status to error.

**Interop** — on Azure Service Bus, messages from non-Modulus publishers instrumented by the Azure SDK are honored via their `Diagnostic-Id` property, so those consumers still join the producer's trace.

## Schema note

`OutboxMessages` gains two nullable columns (`TraceParent`, max 55; `TraceState`, max 512). Consumer-owned migrations apply — generate a follow-up migration after upgrading (see the [migrations guide](https://github.com/adamwyatt34/Modulus/blob/main/src/Modulus.Messaging/Migrations/README.md)). Rows written before the migration (or with no active trace at save time) have `null` context and behave exactly as before.

## Custom headers

`TransportEnvelope.Headers` is a general string-to-string bag: anything you place there rides the same broker mechanisms. Custom `IMessageTransport` implementations should round-trip it; transports that ignore it lose trace propagation but nothing else.

## See Also

- [Message Bus](./message-bus) — the publish path
- [Outbox Pattern](./outbox-pattern) — where the saved context lives
- [Pipeline Behaviors](/mediator/pipeline-behaviors) — `TracingBehavior` for the mediator side
