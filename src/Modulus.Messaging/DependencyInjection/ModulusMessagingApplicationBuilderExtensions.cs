using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Outbox;

namespace Modulus.Messaging.DependencyInjection;

public static class ModulusMessagingApplicationBuilderExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations for whichever Modulus messaging contexts the host has
    /// registered (<see cref="OutboxDbContext"/> and/or <see cref="InboxDbContext"/>). Contexts
    /// that were not registered via <c>AddModulusOutbox</c> / <c>AddModulusInbox</c> are silently
    /// skipped, so this is safe to call from any host regardless of whether messaging is wired.
    /// Non-relational providers (such as <c>UseInMemoryDatabase</c> in tests) are also skipped
    /// so the call is a no-op in that scenario.
    /// </summary>
    /// <remarks>
    /// Modulus.Messaging ships provider-agnostic — consumers add their own
    /// <c>Microsoft.EntityFrameworkCore.SqlServer</c> / <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> /
    /// <c>Microsoft.EntityFrameworkCore.Sqlite</c> reference and generate migrations against their
    /// chosen provider. See <c>src/Modulus.Messaging/Migrations/README.md</c> in the source repo
    /// for the recommended workflow.
    /// </remarks>
    public static async Task UseModulusMessagingMigrationsAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var outbox = provider.GetService<OutboxDbContext>();
        if (outbox is not null && outbox.Database.IsRelational())
            await outbox.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var inbox = provider.GetService<InboxDbContext>();
        if (inbox is not null && inbox.Database.IsRelational())
            await inbox.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
