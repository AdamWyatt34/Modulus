using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.InMemory;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.RabbitMq;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    // Placeholders pointing at local/dev endpoints; not credentials.
    private const string LocalBrokerUri = "amqp://localhost:5672/";
    private const string PlaceholderAsbEndpoint = "Endpoint=sb://placeholder/";

    [Fact]
    public async Task InMemoryTransport_ResolvesWithoutAnyTransportPackage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options => options.Transport = Transport.InMemory);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageTransport>().ShouldBeOfType<InMemoryTransport>();
    }

    [Fact]
    public void RabbitMq_WithoutTransportPackageRegistration_ThrowsInstallGuidance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.RabbitMq;
            options.ConnectionString = LocalBrokerUri;
        });

        using var provider = services.BuildServiceProvider();

        var ex = Should.Throw<InvalidOperationException>(
            () => provider.GetRequiredService<IMessageTransport>());

        ex.Message.ShouldContain("ModulusKit.Messaging.RabbitMq");
        ex.Message.ShouldContain("AddModulusRabbitMqTransport");
    }

    [Fact]
    public async Task RabbitMq_WithTransportRegistered_ResolvesRabbitMqTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.RabbitMq;
            options.ConnectionString = LocalBrokerUri;
        });
        services.AddModulusRabbitMqTransport();

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageTransport>().ShouldBeOfType<RabbitMqTransport>();
    }

    [Fact]
    public void AzureServiceBus_WithoutTransportPackageRegistration_ThrowsInstallGuidance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.AzureServiceBus;
            options.ConnectionString = PlaceholderAsbEndpoint;
        });

        using var provider = services.BuildServiceProvider();

        var ex = Should.Throw<InvalidOperationException>(
            () => provider.GetRequiredService<IMessageTransport>());

        ex.Message.ShouldContain("ModulusKit.Messaging.AzureServiceBus");
        ex.Message.ShouldContain("AddModulusAzureServiceBusTransport");
    }

    [Fact]
    public async Task MessageBus_ResolvesAsTransportMessageBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options => options.Transport = Transport.InMemory);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<Abstractions.IMessageBus>()
            .ShouldBeOfType<TransportMessageBus>();
    }

    [Fact]
    public void PrefetchCount_OutOfRange_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(options => options.PrefetchCount = 0));

        ex.Message.ShouldContain("PrefetchCount");
    }

    [Fact]
    public async Task OutboxNotifier_ResolvesAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options => options.Transport = Transport.InMemory);

        await using var provider = services.BuildServiceProvider();

        var notifier = provider.GetRequiredService<IOutboxNotifier>();
        notifier.ShouldBeSameAs(provider.GetRequiredService<IOutboxNotifier>());
        provider.GetRequiredService<OutboxNotifyingInterceptor>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddModulusOutbox_TransactionalSave_NotifiesOnCommitViaAutoAttachedInterceptor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options => options.Transport = Transport.InMemory);
        services.AddModulusOutbox(options => options.UseSqlite(connection));

        var notifier = new FakeOutboxNotifier();
        services.AddSingleton<IOutboxNotifier>(notifier);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            var store = scope.ServiceProvider.GetRequiredService<Abstractions.IOutboxStore>();
            await store.Save(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Test" });
            notifier.NotifyCount.ShouldBe(0);

            await transaction.CommitAsync();
        }

        notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public void RabbitMq_without_connection_string_throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddModulusMessaging(options =>
            {
                options.Transport = Transport.RabbitMq;
                options.Assemblies.Add(typeof(ServiceCollectionExtensionsTests).Assembly);
                // ConnectionString intentionally omitted
            });
        });

        ex.Message.ShouldContain("ConnectionString");
    }

    [Fact]
    public void AzureServiceBus_without_connection_string_throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddModulusMessaging(options =>
            {
                options.Transport = Transport.AzureServiceBus;
                options.Assemblies.Add(typeof(ServiceCollectionExtensionsTests).Assembly);
                // ConnectionString intentionally omitted
            });
        });

        ex.Message.ShouldContain("ConnectionString");
    }
}
