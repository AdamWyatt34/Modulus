# OpenTelemetry integration

Modulus emits standard `System.Diagnostics` telemetry — no vendor SDK, no required dependency. Wire it to OpenTelemetry in the host.

## What Modulus emits

| Signal | Source / Meter | What you get |
|---|---|---|
| Traces | `Modulus.Mediator` (ActivitySource) | One span per mediator request via `TracingBehavior`: request type, outcome (`success` / `failure` with error count and first error code / `exception`) |
| Traces | `Modulus.Messaging.Outbox` (ActivitySource) | One producer span per outbox dispatch: message id, event type, outcome (`published` / `skipped_unknown_type` / `deserialize_failed` / `retry_pending` / `dead_lettered`), attempt number |
| Metrics | `Modulus.Mediator` (Meter) | `modulus.mediator.handler.duration` histogram (ms) tagged with handler and outcome via `MetricsBehavior` |
| Metrics | `Modulus.Messaging` (Meter) | Outbox and consumer pipeline instruments — see the table below |

### The `Modulus.Messaging` meter

Registered automatically by `AddModulusMessaging(...)`; works with or without metrics DI (`IMeterFactory` is optional).

| Instrument | Type | Tags | Meaning |
|---|---|---|---|
| `modulus.messaging.outbox.messages` | Counter | `outcome` (`published` / `skipped_unknown_type` / `deserialize_failed` / `retry_pending` / `dead_lettered`) | Outbox dispatch attempts by outcome |
| `modulus.messaging.outbox.wakeups` | Counter | `reason` (`signal` / `poll` / `backlog`) | Outbox processor wake-ups. A deployment showing only `poll` is not receiving change notifications (e.g. dedicated-worker topology) and runs at poll-interval latency |
| `modulus.messaging.consumer.handler.duration` | Histogram (ms) | `handler`, `outcome` (`success` / `failure`) | Integration event handler execution time |
| `modulus.messaging.inbox.deduplicated` | Counter | `handler` | Handler executions skipped by inbox idempotency |
| `modulus.messaging.consumer.retries` | Counter | `message_type` | In-process consumer retry attempts |
| `modulus.messaging.consumer.dead_lettered` | Counter | `message_type` | Messages handed to the transport for dead-lettering |

There is deliberately no pending-outbox gauge: an observable gauge would run a database query on the metrics collection thread every scrape. Backlog depth is a readiness concern — use the [messaging health checks](./health-checks#built-in-messaging-health-checks) (`modulus_messaging_outbox`) instead, or chart `outbox.messages` outcome rates.

## Registration

`TracingBehavior` is opt-in, like every pipeline behavior. Register it early so the span wraps the rest of the pipeline:

```csharp
builder.Services.AddModulusMediator();
builder.Services.AddPipelineBehavior(typeof(TracingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
```

Then subscribe the sources in your OpenTelemetry setup:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Modulus.Mediator")
        .AddSource("Modulus.Messaging.Outbox")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Modulus.Mediator")
        .AddMeter("Modulus.Messaging")
        .AddOtlpExporter());
```

With Aspire, the scaffolded ServiceDefaults project already configures the OTLP exporter — adding the two sources and the two meters is all that's needed.
