using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.Messaging.Inbox;

namespace SampleApp.WebApi.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add ... --context InboxDbContext` can
/// construct the ModulusKit.Messaging inbox context against the SQLite provider.
/// The migrations live in this host assembly (see Migrations/Inbox).
/// </summary>
internal sealed class InboxContextFactory : IDesignTimeDbContextFactory<InboxDbContext>
{
    public InboxDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseSqlite(
                "Data Source=sampleapp.db",
                b => b.MigrationsAssembly(typeof(InboxContextFactory).Assembly.GetName().Name))
            .Options;

        return new InboxDbContext(options);
    }
}
