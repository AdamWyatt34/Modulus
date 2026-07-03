# OpenTelemetry integration

Modulus emits standard `System.Diagnostics` telemetry — no vendor SDK, no required dependency. Wire it to OpenTelemetry in the host.

## What Modulus emits

| Signal | Source / Meter | What you get |
|---|---|---|
| Traces | `Modulus.Mediator` (ActivitySource) | One span per mediator request via `TracingBehavior`: request type, outcome (`success` / `failure` with error count and first error code / `exception`) |
| Traces | `Modulus.Messaging.Outbox` (ActivitySource) | One producer span per outbox dispatch: message id, event type, outcome (`published` / `skipped_unknown_type` / `retry_pending` / `dead_lettered`), attempt number |
| Metrics | `Modulus.Mediator` (Meter) | `modulus.mediator.handler.duration` histogram (ms) tagged with handler and outcome via `MetricsBehavior` |

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
        .AddOtlpExporter());
```

With Aspire, the scaffolded ServiceDefaults project already configures the OTLP exporter — adding the two sources and the meter is all that's needed.
