# SampleApp — Modulus reference application

A known-good modular monolith scaffolded by the **Modulus CLI itself** (dogfooding), exercising
all three ModulusKit pillars end to end:

1. **Mediator** — `CreateOrder` command (+ FluentValidation validator) and `GetOrder` query flow
   through the custom CQRS pipeline (`UnhandledException → Logging → Metrics → Validation →
   UnitOfWork → Handler`), returning `Result` / `Result<T>` instead of throwing.
2. **Messaging with transactional outbox/inbox** — the `CreateOrder` handler writes an
   `OrderPlaced` integration event to the outbox via `IOutboxStore`; the `OutboxProcessor`
   background service publishes it over the in-house **in-memory transport** (no MassTransit),
   and the Notifications module consumes it with an `IIntegrationEventHandler<OrderPlaced>`.
   The inbox store provides consumer idempotency.
3. **Source generators** — `AddModulusHandlers()`, `AddAllModules()`, and
   `MapAllModuleEndpoints()` in `Program.cs` are generated at compile time by
   `Modulus.Generators` from the referenced module assemblies. No manual handler registration
   anywhere. `Modulus.Analyzers` (MOD001–MOD005) runs over the application layer.

Two modules talk to each other **only** through an integration event:

```
POST /api/orders
  └─ CreateOrder (Orders.Application)          ── mediator pipeline (validate → handle → UoW)
       ├─ Order aggregate persisted             (Orders.Infrastructure, SQLite)
       └─ OrderPlaced saved to outbox           (IOutboxStore → OutboxMessages table)
            └─ OutboxProcessor publishes        (in-memory transport)
                 └─ OrderPlacedHandler          (Notifications.Infrastructure) logs the event
```

## How this sample was generated

Everything was scaffolded with the CLI from the repository root, then lightly hand-finished:

```powershell
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init SampleApp --output samples --no-git
$sln = "samples/SampleApp/SampleApp.slnx"
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-module Orders --solution $sln
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-module Notifications --solution $sln
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-entity Order --module Orders --solution $sln --aggregate --properties "CustomerName:string,Total:decimal"
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-command CreateOrder --module Orders --solution $sln --result-type Guid
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-query GetOrder --module Orders --solution $sln --result-type OrderDto
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-event OrderPlaced --module Orders --solution $sln --properties "OrderId:Guid,Total:decimal"
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-consumer OrderPlaced --module Notifications --solution $sln
```

Hand-finishing on top of the scaffold (the parts a real user writes after scaffolding):

- Implemented `CreateOrderHandler` (repository + outbox), `GetOrderHandler` (read-side `IQueryDb`),
  `CreateOrderValidator` rules, `OrderDto`, and the `PlaceOrder` / `GetOrderById` endpoints.
- Enabled the messaging block in `Program.cs` (it ships commented-out) and referenced the two
  module `*.Infrastructure` projects from `SampleApp.WebApi` so the source generators can
  discover the modules and handlers.
- Switched the module DbContexts and the outbox/inbox from SQL Server to **SQLite** so the sample
  runs with zero infrastructure.
- Generated the outbox/inbox EF Core migrations (the documented per-host workflow —
  `ModulusKit.Messaging` ships provider-agnostic):

  ```powershell
  cd samples/SampleApp/src/SampleApp.WebApi
  dotnet ef migrations add InitialOutbox --context OutboxDbContext --output-dir Migrations/Outbox
  dotnet ef migrations add InitialInbox  --context InboxDbContext  --output-dir Migrations/Inbox
  ```

  `Persistence/OutboxContextFactory.cs` and `Persistence/InboxContextFactory.cs` are the
  design-time factories that make `dotnet ef` work against the SQLite provider.

## ProjectReference vs NuGet packages

**This sample references the ModulusKit source projects in this repository directly** (via
`ProjectReference` into `../../src/*`) so it always builds against the current code in CI —
the 2.x packages are not yet published to NuGet.

A real application scaffolded by `modulus init` gets `PackageReference`s instead:

| In this sample (ProjectReference)         | In a real app (PackageReference)     |
| ----------------------------------------- | ------------------------------------ |
| `src/Modulus.Mediator.Abstractions`       | `ModulusKit.Mediator.Abstractions`   |
| `src/Modulus.Mediator`                    | `ModulusKit.Mediator`                |
| `src/Modulus.Messaging.Abstractions`      | `ModulusKit.Messaging.Abstractions`  |
| `src/Modulus.Messaging`                   | `ModulusKit.Messaging`               |
| `src/Modulus.Generators` (as Analyzer)    | `ModulusKit.Generators`              |
| `src/Modulus.Analyzers` (as Analyzer)     | `ModulusKit.Analyzers`               |

Each converted `.csproj` carries a `<!-- NuGet equivalent: ... -->` comment at the swap site.
`Directory.Build.targets` here is intentionally empty — it stops the repository root's MSBuild
files from leaking into the sample; a real app outside this repo doesn't need it.

## How messaging and the outbox are wired

`src/SampleApp.WebApi/Program.cs`:

```csharp
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Assemblies.Add(typeof(OrderPlaced).Assembly);          // event types (Orders.Integration)
    options.Assemblies.Add(typeof(NotificationsModule).Assembly);  // consumers (Notifications.Infrastructure)
});
builder.Services.AddModulusOutbox(o => o.UseSqlite(...));  // OutboxDbContext
builder.Services.AddModulusInbox(o => o.UseSqlite(...));   // InboxDbContext + idempotency store

var app = builder.Build();
await app.UseModulusMessagingMigrationsAsync();            // applies Migrations/Outbox + Migrations/Inbox
```

- `Messaging:Transport` is `InMemory` in `appsettings.json`; switching to RabbitMQ or Azure
  Service Bus is configuration plus one `AddModulus*Transport()` call from the matching package.
- The outbox/inbox live in `sampleapp.db`; each module has its own SQLite database
  (`sampleapp-orders.db`, `sampleapp-notifications.db`) created via `EnsureCreated()` in
  Development. Real apps manage per-module EF migrations instead.
- Sample-scale caveat: `IOutboxStore.Save` commits on the messaging context while the order row
  commits through the mediator's `UnitOfWorkBehavior` on the module context — two SQLite files,
  two transactions. Point both at the same database (and share the connection) when you need
  strict atomicity.

## Run it

```powershell
cd samples/SampleApp
dotnet build SampleApp.slnx
dotnet run --project src/SampleApp.WebApi
```

Then (Scalar API reference is at `/scalar/v1` in Development):

```powershell
# Place an order — returns 201 with the new order id, and the Notifications module
# logs "received OrderPlaced" once the outbox processor has dispatched the event.
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/orders `
  -ContentType application/json -Body '{ "customerName": "Ada Lovelace", "total": 42.50 }'

# Read it back
Invoke-RestMethod http://localhost:5000/api/orders/<id>
```

Run the sample's tests with `dotnet test SampleApp.slnx`.
