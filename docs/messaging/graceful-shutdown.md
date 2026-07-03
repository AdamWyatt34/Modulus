# Graceful Shutdown

What happens to in-flight messages when a Modulus messaging host stops, and how to configure orchestrators around it.

## Stop Ordering

.NET stops hosted services in **reverse registration order**. `AddModulusMessaging` registers the consumer host before the outbox processor, so shutdown proceeds:

1. **`OutboxProcessor` stops first** — the poll loop observes cancellation and stops fetching new batches. A dispatch pass that is mid-flight finishes its current message; unpublished rows simply stay in the outbox table and are dispatched after the next start. **Nothing is lost**: the outbox is durable by design.
2. **`TransportConsumerHost` stops second** — it calls the transport's `StopConsumingAsync`, which cancels the broker subscription and drains in-flight handler work.
3. **The transport disposes last** — connections close after consumers have stopped.

## In-Flight Semantics Per Transport

| Transport | In-flight message on shutdown |
|---|---|
| **RabbitMQ** | Unacknowledged deliveries return to the queue when the connection closes and are **redelivered** after restart. The [inbox](./inbox-pattern) skips handlers that already completed; a handler interrupted mid-execution leaves a reservation that goes stale and is re-executed on redelivery. |
| **Azure Service Bus** | The message lock expires (processors stop renewing on stop) and the message is **redelivered** to the subscription. Same inbox semantics as above. |
| **In-memory** | Buffered messages are drained during `StopConsumingAsync` within the shutdown window; anything beyond it is **dropped** (in-process transport, no durability — pair with the outbox for anything that matters). |

Net effect with outbox + inbox + a broker transport: shutdown at any point is **at-least-once with per-handler idempotency** — no lost messages, no double-executed handlers.

## Shutdown Window

The default host shutdown timeout is 30 seconds (`HostOptions.ShutdownTimeout`). Handlers that can run long should stay comfortably inside it; the in-process retry backoff (`ConsumerRetry`) also counts against the window, so a message stuck in a retry loop during shutdown will be cut off and redelivered later — which is fine, and exactly what the reservation stale-takeover handles.

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));
```

## Kubernetes Guidance

- Set `terminationGracePeriodSeconds` **above** the host's `ShutdownTimeout` (e.g. 45s over a 30s timeout) so the kubelet doesn't SIGKILL mid-drain.
- A SIGKILL (grace period exceeded, OOM) is equivalent to a crash: unacked broker messages redeliver, inbox reservations go stale and are taken over, unpublished outbox rows dispatch after restart. The system converges; you only lose the time.
- Use the scaffolded `/readyz` endpoint (with the [messaging health checks](/recipes/health-checks#built-in-messaging-health-checks)) as the readiness probe so traffic stops before shutdown begins in rolling updates.

## See Also

- [Inbox Pattern](./inbox-pattern) — reservation stale-takeover semantics
- [Outbox Pattern](./outbox-pattern) — durable publishing
- [Dead-Letter Queues](./dead-letter-queues) — where exhausted messages land
