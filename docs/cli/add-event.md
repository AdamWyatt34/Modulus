# modulus add-event

Scaffolds an integration event inside a module's `Integration` project. Integration events are the contract for cross-module and cross-process communication: they are published through `IMessageBus`, stored in the transactional outbox, and delivered to consumers via MassTransit. They derive from the `IntegrationEvent` base record, which supplies `EventId`, `OccurredOn`, and `CorrelationId`.

## Synopsis

```bash
modulus add-event <event-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<event-name>` | PascalCase name for the event (e.g., `OrderPlaced`, `OrderShipped`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Module that owns the event. The file is created in that module's `Integration` project. | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |
| `--properties, -p <list>` | Comma-separated payload properties in `Name:Type` format. Each becomes a positional record parameter. | None |

## Generated Output

Running `modulus add-event OrderShipped --module Orders --properties "OrderId:Guid,ShippedOn:DateTime"` generates one file:

`src/Modules/Orders/src/Orders.Integration/IntegrationEvents/OrderShipped.cs`

```csharp
using Modulus.Messaging.Abstractions;

namespace EShop.Orders.Integration.IntegrationEvents;

public sealed record OrderShipped(Guid OrderId, DateTime ShippedOn) : IntegrationEvent;
```

When `--properties` is omitted, a parameterless record is generated:

```csharp
public sealed record OrderShipped : IntegrationEvent;
```

> The explicit `using Modulus.Messaging.Abstractions;` is required because the module's `Integration` project references those types transitively through `BuildingBlocks.Integration` rather than via a global using.

## Examples

**Event with a payload:**

```bash
modulus add-event OrderPlaced --module Orders --properties "OrderId:Guid,Total:decimal"
```

**Marker event (no payload):**

```bash
modulus add-event CatalogReindexRequested --module Catalog
```

## Publishing the event

Inject `IMessageBus` into a handler and publish:

```csharp
await messageBus.Publish(new OrderPlaced(order.Id, order.Total), cancellationToken);
```

With the outbox configured, the event is persisted in the same transaction as your business data and dispatched by the background outbox processor.

## See Also

- [modulus add-consumer](./add-consumer) -- Scaffold a handler for an integration event
- [modulus add-command](./add-command) -- Scaffold a command that may publish events
- [Messaging & Outbox](/messaging/) -- How integration events flow through the outbox and MassTransit
