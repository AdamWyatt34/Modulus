Three files created at `.claude/skills/masstransit/`:

**`SKILL.md`** — Quick reference covering the key abstractions (`IIntegrationEvent`, `IMessageBus`, `IOutboxStore`), transport selection table, outbox-within-transaction pattern, and links to references.

**`references/patterns.md`** — Detailed patterns with code examples from the actual codebase:
- Event definition (extend `IntegrationEvent`, not `IIntegrationEvent`)
- Handler implementation (sealed + primary constructor)
- Outbox usage (atomic commit with domain write)
- Inbox/idempotency mechanics (per-handler deduplication on `EventId + HandlerTypeName`)
- Three WARNINGs: publishing via `IBus` directly, using domain events for cross-module comms, throwing from handlers

**`references/workflows.md`** — Step-by-step checklists for:
- Adding a new integration event end-to-end (7-step checklist with code at each step)
- Switching transport (InMemory → RabbitMQ/Azure Service Bus config patterns)
- Writing messaging tests (outbox store tests + handler delivery tests)
- Troubleshooting (handler never called, outbox stuck, connection string errors, duplicate execution in tests)