using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.DependencyInjection;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Outbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class UseModulusMessagingMigrationsAsyncTests
{
    [Fact]
    public async Task NoContextsRegistered_doesNotThrow()
    {
        var services = new ServiceCollection();
        await using var host = new TestHost(services.BuildServiceProvider());

        await host.UseModulusMessagingMigrationsAsync();
    }

    [Fact]
    public async Task OutboxRegistered_doesNotThrow()
    {
        // InMemory provider treats MigrateAsync as a no-op (EnsureCreated). The test asserts
        // that registering only one of the two contexts is safe: the other lookup must skip
        // silently rather than throw.
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(o =>
            o.UseInMemoryDatabase(nameof(OutboxRegistered_doesNotThrow)));

        await using var host = new TestHost(services.BuildServiceProvider());

        await host.UseModulusMessagingMigrationsAsync();
    }

    [Fact]
    public async Task InboxRegistered_doesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddDbContext<InboxDbContext>(o =>
            o.UseInMemoryDatabase(nameof(InboxRegistered_doesNotThrow)));

        await using var host = new TestHost(services.BuildServiceProvider());

        await host.UseModulusMessagingMigrationsAsync();
    }

    [Fact]
    public async Task BothContextsRegistered_doesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(o =>
            o.UseInMemoryDatabase($"outbox-{nameof(BothContextsRegistered_doesNotThrow)}"));
        services.AddDbContext<InboxDbContext>(o =>
            o.UseInMemoryDatabase($"inbox-{nameof(BothContextsRegistered_doesNotThrow)}"));

        await using var host = new TestHost(services.BuildServiceProvider());

        await host.UseModulusMessagingMigrationsAsync();
    }

    [Fact]
    public async Task NullHost_throws()
    {
        IHost host = null!;
        await Should.ThrowAsync<ArgumentNullException>(() => host.UseModulusMessagingMigrationsAsync());
    }

    [Fact]
    public async Task RelationalProvider_doesNotThrow_whenNoMigrationsExist()
    {
        // Modulus.Messaging is provider-agnostic and ships no migrations; consumers generate them.
        // Verify the helper still completes cleanly on a relational provider when the migration
        // set is empty (MigrateAsync becomes a no-op).
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(connection));
        services.AddDbContext<InboxDbContext>(o => o.UseSqlite(connection));

        await using var host = new TestHost(services.BuildServiceProvider());

        await host.UseModulusMessagingMigrationsAsync();
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() => (Services as IDisposable)?.Dispose();
        public ValueTask DisposeAsync() => (Services as IAsyncDisposable)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
