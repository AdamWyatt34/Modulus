# modulus dlq

Inspects and replays **broker** dead-letter queues — the messages that exhausted consumer retries. This is the consumer-side counterpart to [`modulus outbox`](./outbox), which operates on the publisher-side database outbox.

## Usage

```bash
# List dead-lettered messages on a RabbitMQ endpoint
modulus dlq list --transport rabbitmq --endpoint checkout

# Replay one message by id (the integration event's EventId)
modulus dlq replay --transport rabbitmq --endpoint checkout --message-id 8f3c...

# Replay everything (bounded by --max)
modulus dlq replay --transport rabbitmq --endpoint checkout --all

# Azure Service Bus DLQs are per topic/subscription, so the event type selects the topic
modulus dlq list --transport azureservicebus --endpoint checkout --event MyApp.Orders.Integration.OrderPlacedEvent
```

## Options

| Option | Description |
|---|---|
| `--transport <rabbitmq\|azureservicebus>` | Required. The broker to talk to. |
| `--connection-string <VALUE>` | Broker connection string. Default: `Messaging:ConnectionString` from `--config`. |
| `--config <PATH>` | Path to appsettings.json (default: `./appsettings.json`). |
| `--endpoint <NAME>` | Endpoint whose DLQ to operate on. Default: `Messaging:EndpointName` from `--config`. |
| `--event <FULL_TYPE_NAME>` | Required for Azure Service Bus: selects the topic whose subscription DLQ is read. |
| `--max <N>` | Messages to read (list) or examine (replay). Default 50. |
| `--message-id <ID>` \| `--all` | Replay target — exactly one must be given. |

## Semantics Per Transport

### RabbitMQ

- Operates on the `{endpoint}.dead-letter` queue ([naming conventions](/messaging/dead-letter-queues)).
- **`list` is a destructive peek**: RabbitMQ has no true peek, so messages are read unacknowledged and requeued afterwards. Nothing is lost, but delivery order resets and redelivery counters bump.
- `replay` re-publishes each message to the exchange it first died from (`x-first-death-exchange`, falling back to its event type's exchange) with **publisher confirmations** — the dead-lettered copy is acknowledged only after the broker confirms the re-publish, so a failed replay leaves the message in the DLQ.

### Azure Service Bus

- Operates on the subscription's built-in dead-letter sub-queue of the `--event` topic.
- `list` is a true peek (non-destructive).
- `replay` clones the message (body, MessageId, application properties) and sends it to the topic — Service Bus has no native resubmit, so broker-set system properties (enqueue time, sequence number) are new on the replayed copy.

## Replay and Idempotency

A replayed message carries its original `MessageId`/`EventId`, so the [inbox](/messaging/inbox-pattern) skips every handler that already succeeded — only the handlers that never completed re-run. Replaying is safe to repeat.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Listed successfully, or replay completed. |
| `1` | Connection could not be resolved, broker error, or `--message-id` not found within `--max`. |
