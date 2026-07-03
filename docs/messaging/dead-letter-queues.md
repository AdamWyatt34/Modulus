# Dead-Letter Queues

Where messages land when a consumer gives up — the naming conventions per transport, how to inspect them, and how to replay.

## When Messages Dead-Letter

The consumer pipeline retries a failing handler in-process per `MessagingOptions.ConsumerRetry` (default 5 attempts with exponential backoff). When retries are exhausted — or a message body cannot be deserialized at all — the dispatcher hands the message back to the transport for dead-lettering. Unknown message types are **not** dead-lettered; they are acknowledged and dropped with a warning.

## RabbitMQ Conventions

| Entity | Name |
|---|---|
| Dead-letter exchange | `{endpoint}.dlx` |
| Dead-letter queue | `{endpoint}.dead-letter` |

The endpoint's consume queue is declared with `x-dead-letter-exchange = {endpoint}.dlx`; a rejected message (`basic.nack`, `requeue: false`) routes through it into `{endpoint}.dead-letter`. RabbitMQ stamps `x-death` headers with the original exchange, queue, and reason — `modulus dlq replay` uses `x-first-death-exchange` to send the message home.

Inspect with native tooling:

```bash
# Management UI: Queues -> {endpoint}.dead-letter -> Get messages
rabbitmqadmin get queue={endpoint}.dead-letter count=10 requeue=true
```

## Azure Service Bus Conventions

Service Bus has DLQs built in: every subscription owns a dead-letter sub-queue at `{topic}/Subscriptions/{subscription}/$DeadLetterQueue`. Modulus dead-letters with reason **`RetriesExhausted`**. There are no custom entities to provision.

Inspect with native tooling: Azure Portal (Service Bus Explorer on the subscription → Dead-letter), or `ServiceBusReceiver` with `SubQueue.DeadLetter`.

## Inspecting and Replaying with the CLI

[`modulus dlq`](/cli/dlq) wraps both conventions:

```bash
modulus dlq list --transport rabbitmq --endpoint checkout
modulus dlq replay --transport rabbitmq --endpoint checkout --all
```

Replayed messages keep their `EventId`, so the [inbox](./inbox-pattern) re-runs only the handlers that never completed. Fix the bug first, deploy, then replay.

## Don't Forget the Other Side

Broker DLQs hold **consumer-side** failures. Publisher-side failures (the broker was unreachable when the outbox tried to publish) live in the outbox table instead — inspect those with [`modulus outbox list-failed`](/cli/outbox) and requeue with `modulus outbox retry`.

## See Also

- [Transports](./transports) — full topology reference
- [Graceful Shutdown](./graceful-shutdown) — in-flight semantics
- [`modulus dlq`](/cli/dlq) · [`modulus outbox`](/cli/outbox)
