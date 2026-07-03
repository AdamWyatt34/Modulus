# modulus outbox

Inspects and operates the **transactional outbox** database — the publisher-side messages that failed dispatch and stopped being retried. This is the counterpart to [`modulus dlq`](./dlq), which operates on broker dead-letter queues (the consumer side).

## Usage

```bash
# Show messages that exhausted their dispatch retries
modulus outbox list-failed

# Reset a message so the outbox processor retries it on the next poll
modulus outbox retry 8f3c2a1e-...

# Permanently delete a message
modulus outbox purge 8f3c2a1e-...
```

## Subcommands

| Subcommand | Description |
|---|---|
| `list-failed [--max-attempts N]` | List messages whose attempt count is at or above the threshold (default 5 — match your `MessagingOptions.RetryPolicy.MaxAttempts`). Shows id, attempts, creation time, event type, and last error. |
| `retry <messageId>` | Reset the attempt counter and clear the last error. The `OutboxProcessor` picks the message up on its next poll. |
| `purge <messageId>` | Permanently delete the message. |

## Common Options

| Option | Description |
|---|---|
| `--connection-string <VALUE>` | Database connection string. Default: `Messaging:ConnectionString` from `--config`. |
| `--config <PATH>` | Path to appsettings.json (default: `./appsettings.json` in the current directory). |
| `--provider <SqlServer\|Sqlite>` | EF Core provider for the outbox database (default: SqlServer). |

Connection resolution order: `--connection-string` → `Messaging:ConnectionString` in the `--config` file → `./appsettings.json`.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (including an empty `list-failed`). |
| `1` | Connection could not be resolved, or the message id was not found. |

## See Also

- [Outbox Pattern](/messaging/outbox-pattern) — how messages get here
- [`modulus dlq`](./dlq) — the broker-side counterpart
