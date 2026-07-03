# modulus add-consumer

Scaffolds an `IIntegrationEventHandler<TEvent>` that consumes an integration event, placing it in the consuming module's `Infrastructure` project. The command locates the event in its owning module's `Integration` project, generates the handler, and **automatically wires the cross-module project reference** so the scaffold compiles immediately.

The generated handler is discovered and registered automatically by the Modulus source generator and MassTransit consumer wiring — no manual DI registration is required.

## Synopsis

```bash
modulus add-consumer <event-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<event-name>` | PascalCase name of the integration event to consume (e.g., `OrderPlaced`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Consuming module that hosts the handler. | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |
| `--event-module <name>` | Module that owns the event. Use to disambiguate when the same event name exists in more than one module. | Auto-detected |

## How the event is located

The command searches each module's `Integration` project for a file named `<event-name>.cs`:

- **Found in exactly one module** — that module becomes the event's source.
- **Found in multiple modules** — the command fails and asks you to pass `--event-module`.
- **Not found** — the command fails and suggests running [`modulus add-event`](./add-event) first.

## Generated Output

Running `modulus add-consumer OrderShipped --module Shipping` (where `OrderShipped` lives in the `Orders` module) generates one file and edits one project file.

### Handler class

`src/Modules/Shipping/src/Shipping.Infrastructure/IntegrationEventHandlers/OrderShippedHandler.cs`

```csharp
using Modulus.Messaging.Abstractions;
using EShop.Orders.Integration.IntegrationEvents;

namespace EShop.Shipping.Infrastructure.IntegrationEventHandlers;

public sealed class OrderShippedHandler : IIntegrationEventHandler<OrderShipped>
{
    public Task Handle(OrderShipped @event, CancellationToken cancellationToken = default)
    {
        // TODO: Implement integration event handling logic
        return Task.CompletedTask;
    }
}
```

### Auto-wired project reference

A `ProjectReference` from the consuming `Infrastructure` project to the source module's `Integration` project is added to `Shipping.Infrastructure.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\Orders\src\Orders.Integration\Orders.Integration.csproj" />
</ItemGroup>
```

This is the only cross-module dependency the architecture permits (`Integration` projects are the public contract surface — enforced by analyzer **MOD001**). The edit is idempotent: if the reference already exists, it is left unchanged.

## Why the handler lives in Infrastructure

`IIntegrationEventHandler<TEvent>` comes from `Modulus.Messaging.Abstractions`, which a module only reaches through `BuildingBlocks.Infrastructure`. The `Application` project does not reference messaging, so consumers belong in `Infrastructure`.

## After scaffolding

Make sure the consuming module's `Infrastructure` assembly is registered for messaging so the consumer receives events:

```csharp
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(ShippingModule).Assembly);
});
```

## Examples

**Consume an event whose source is auto-detected:**

```bash
modulus add-consumer OrderShipped --module Shipping
```

**Disambiguate when two modules publish the same event name:**

```bash
modulus add-consumer OrderShipped --module Shipping --event-module Orders
```

## See Also

- [modulus add-event](./add-event) -- Scaffold the integration event being consumed
- [Messaging & Outbox](/messaging/) -- Outbox, inbox, and idempotent consumer delivery
