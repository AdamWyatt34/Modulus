using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.RabbitMq.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.RabbitMq.IntegrationTests;

[Collection(RabbitMqCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RabbitMqTransportIntegrationTests(RabbitMqContainerFixture rabbitMq)
{
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(30);

    private ServiceCollection BuildServices(string endpointName, Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.RabbitMq;
            options.ConnectionString = rabbitMq.ConnectionString;
            options.EndpointName = endpointName;
            options.Assemblies.Add(typeof(RoundTripEvent).Assembly);
            configure?.Invoke(options);
        });
        services.AddModulusRabbitMqTransport();
        return services;
    }

    private static async Task<StartedHost> StartHost(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var started = new List<IHostedService>();

        // Only the consumer host: outbox polling is exercised by its own tests, and skipping it
        // avoids requiring an OutboxDbContext here.
        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            if (hostedService is not OutboxProcessor)
            {
                await hostedService.StartAsync(CancellationToken.None);
                started.Add(hostedService);
            }
        }

        return new StartedHost(provider, started);
    }

    private sealed record StartedHost(ServiceProvider Provider, List<IHostedService> Started) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var i = Started.Count - 1; i >= 0; i--)
                await Started[i].StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private static async Task WaitFor(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + BrokerTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Condition not met within {BrokerTimeout.TotalSeconds}s: {because}");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Publish_RoundTripsThroughBroker_ToHandler()
    {
        RoundTripHandler.Handled.Clear();
        await using var host = await StartHost(BuildServices("it-roundtrip"));

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var eventId = Guid.NewGuid();
        await messageBus.Publish(new RoundTripEvent
        {
            EventId = eventId,
            Value = 42,
            CorrelationId = "it-corr"
        });

        await WaitFor(() => !RoundTripHandler.Handled.IsEmpty, "published event should reach the handler");

        RoundTripHandler.Handled.TryPeek(out var received).ShouldBeTrue();
        received!.Value.ShouldBe(42);
        received.EventId.ShouldBe(eventId);
        received.CorrelationId.ShouldBe("it-corr");
    }

    [Fact]
    public async Task FailingHandler_ExhaustsRetries_MessageLandsInDeadLetterQueue()
    {
        DeadLetterHandler.Reset();
        await using var host = await StartHost(BuildServices("it-dlx", options =>
        {
            options.ConsumerRetry.MaxAttempts = 2;
            options.ConsumerRetry.InitialInterval = TimeSpan.Zero;
            options.ConsumerRetry.MaxInterval = TimeSpan.Zero;
            options.ConsumerRetry.IntervalIncrement = TimeSpan.Zero;
        }));

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.Publish(new DeadLetterEvent { Value = 13 });

        // Inspect the dead-letter queue directly through a raw client connection.
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        BasicGetResult? deadLettered = null;
        await WaitFor(
            () =>
            {
                deadLettered = channel.BasicGetAsync("it-dlx.dead-letter", autoAck: true).GetAwaiter().GetResult();
                return deadLettered is not null;
            },
            "message should land in the dead-letter queue after retries are exhausted");

        DeadLetterHandler.Attempts.ShouldBe(2);
        deadLettered!.BasicProperties.Type.ShouldBe(typeof(DeadLetterEvent).FullName);
    }

    [Fact]
    public async Task InboxEnabled_DuplicateDelivery_HandlerRunsOnce()
    {
        InboxDedupHandler.Handled.Clear();

        // Deliberately not disposed: a consumer callback may still hold the connection when the
        // host stops, and SqliteConnection.Dispose races it (NRE in Close). The in-memory
        // database dies with the test process.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = BuildServices("it-inbox");
        services.AddModulusInbox(options => options.UseSqlite(connection));

        await using var host = await StartHost(services);

        using (var schemaScope = host.Provider.CreateScope())
        {
            schemaScope.ServiceProvider.GetRequiredService<InboxDbContext>().Database.EnsureCreated();
        }

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Re-publishing the same EventId after the first copy is fully processed models broker
        // redelivery. (Two copies in flight concurrently can both execute — the inbox guarantee
        // is per processed delivery, exactly as it was under MassTransit.)
        var @event = new InboxDedupEvent { EventId = Guid.NewGuid(), Value = 7 };
        await messageBus.Publish(@event);

        await WaitFor(() => !InboxDedupHandler.Handled.IsEmpty, "first delivery should reach the handler");

        await messageBus.Publish(@event);

        // Give the duplicate time to arrive and be deduplicated, then assert exactly once.
        await Task.Delay(2000);
        InboxDedupHandler.Handled.Count.ShouldBe(1);
    }
}
