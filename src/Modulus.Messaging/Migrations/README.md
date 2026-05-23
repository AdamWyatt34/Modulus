# Outbox & Inbox migrations

`ModulusKit.Messaging` ships **provider-agnostic** — it does not pin SQL Server, PostgreSQL, or any other EF Core provider. You generate migrations once in your own host project against the provider you actually use.

## One-time setup

1. Pick a provider in your host project and install the matching EF Core packages:

   ```powershell
   # SQL Server
   dotnet add package Microsoft.EntityFrameworkCore.SqlServer
   dotnet add package Microsoft.EntityFrameworkCore.Design

   # PostgreSQL
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
   dotnet add package Microsoft.EntityFrameworkCore.Design

   # SQLite
   dotnet add package Microsoft.EntityFrameworkCore.Sqlite
   dotnet add package Microsoft.EntityFrameworkCore.Design
   ```

2. Wire the contexts in `Program.cs`:

   ```csharp
   builder.Services.AddModulusMessaging(o =>
   {
       o.Transport = Transport.RabbitMq;
       o.ConnectionString = builder.Configuration.GetConnectionString("Rabbit");
       o.Assemblies.Add(typeof(Program).Assembly);
   });

   builder.Services.AddModulusOutbox(o =>
       o.UseSqlServer(builder.Configuration.GetConnectionString("Messaging")));
   builder.Services.AddModulusInbox(o =>
       o.UseSqlServer(builder.Configuration.GetConnectionString("Messaging")));
   ```

3. Add an `IDesignTimeDbContextFactory<T>` for each context so `dotnet ef` can construct them:

   ```csharp
   // OutboxContextFactory.cs in your host
   internal sealed class OutboxContextFactory : IDesignTimeDbContextFactory<OutboxDbContext>
   {
       public OutboxDbContext CreateDbContext(string[] args)
       {
           var options = new DbContextOptionsBuilder<OutboxDbContext>()
               .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Messaging;")
               .Options;
           return new OutboxDbContext(options);
       }
   }
   ```

4. Generate the initial migrations:

   ```powershell
   dotnet ef migrations add InitialOutbox --context OutboxDbContext --output-dir Migrations/Outbox
   dotnet ef migrations add InitialInbox  --context InboxDbContext  --output-dir Migrations/Inbox
   ```

5. Apply them at startup with the bundled helper. It is safe to call regardless of whether the
   contexts are registered — it silently no-ops when they are not:

   ```csharp
   var app = builder.Build();
   await app.UseModulusMessagingMigrationsAsync();
   app.Run();
   ```

## Schema reference

Both contexts are defined in this package and target the same database (or two separate ones — your choice). The relevant tables are:

| Table                     | Source                                  |
| ------------------------- | --------------------------------------- |
| `OutboxMessages`          | `OutboxDbContext` → `OutboxMessage`     |
| `InboxMessages`           | `InboxDbContext` → `InboxMessage`       |
| `InboxMessageConsumers`   | `InboxDbContext` → composite (Id, Name) |

The polling indexes (`ProcessedAt, CreatedAt` on Outbox; `ProcessedOnUtc, OccurredOnUtc` on Inbox) are configured in `OnModelCreating` and will be created automatically when you generate the migrations.

## Re-generating after schema changes

Future schema changes (added columns, indexes, etc.) ship as updates to the entity classes in this package. After updating to a newer `ModulusKit.Messaging` version, generate a follow-up migration in your host project:

```powershell
dotnet ef migrations add MessagingV2 --context OutboxDbContext --output-dir Migrations/Outbox
```

`UseModulusMessagingMigrationsAsync` will pick it up at the next startup.
