# modulus inbox

Operates the **consumer inbox** database — the per-message, per-handler idempotency records the [Inbox Pattern](/messaging/inbox-pattern) keeps. Its one job today is retention: bulk-deleting rows old enough that the broker can no longer redeliver their messages.

## Usage

```bash
# Report how many inbox messages a 7-day retention purge would remove...
modulus inbox purge --older-than-days 7

# ...then actually delete them (and their per-handler consumer rows)
modulus inbox purge --older-than-days 7 --confirm
```

## Subcommands

| Subcommand | Description |
|---|---|
| `purge [--older-than-days N] [--batch-size N] [--confirm]` | Bulk-delete inbox messages that occurred more than N days ago (default 7, minimum 1), along with their `InboxMessageConsumers` rows, in batches (default 500) until drained. **Without `--confirm` it only reports the matching row count.** For automatic cleanup, enable `MessagingOptions.Retention` instead — see [Inbox Pattern § Retention](/messaging/inbox-pattern#retention). |

::: warning Purged rows leave the deduplication window
The inbox is the messaging layer's duplicate-delivery memory. If the broker redelivers a message after its inbox row is purged, every handler runs again. Only purge past your broker's **maximum redelivery horizon — dead-letter replays included** (a `modulus dlq replay` of a months-old message re-executes handlers if the inbox rows are gone and your handlers aren't otherwise idempotent).
:::

## Common Options

| Option | Description |
|---|---|
| `--connection-string <VALUE>` | Database connection string. Default: `ConnectionStrings:Default` from `--config`. |
| `--config <PATH>` | Path to appsettings.json (default: `./appsettings.json` in the current directory). |
| `--provider <SqlServer\|Sqlite>` | EF Core provider for the inbox database (default: SqlServer). |

Connection resolution follows the same order as [`modulus outbox`](./outbox#common-options): explicit flag, then `ConnectionStrings:Default`, then the legacy `Messaging:ConnectionString` fallback with a warning.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (including a preview run without `--confirm`). |
| `1` | Connection could not be resolved, or an option was out of range. |

## See Also

- [Inbox Pattern](/messaging/inbox-pattern) — what these rows guarantee
- [`modulus outbox`](./outbox) — the publisher-side counterpart
