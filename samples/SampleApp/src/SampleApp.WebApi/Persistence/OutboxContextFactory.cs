using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.Messaging.Outbox;

namespace SampleApp.WebApi.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add ... --context OutboxDbContext` can
/// construct the ModulusKit.Messaging outbox context against the SQLite provider.
/// The migrations live in this host assembly (see Migrations/Outbox).
/// </summary>
internal sealed class OutboxContextFactory : IDesignTimeDbContextFactory<OutboxDbContext>
{
    public OutboxDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(
                "Data Source=sampleapp.db",
                b => b.MigrationsAssembly(typeof(OutboxContextFactory).Assembly.GetName().Name))
            .Options;

        return new OutboxDbContext(options);
    }
}
