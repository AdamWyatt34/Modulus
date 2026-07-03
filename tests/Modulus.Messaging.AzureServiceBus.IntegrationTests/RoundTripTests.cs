using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.AzureServiceBus.IntegrationTests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests;

[Collection(ServiceBusCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RoundTripTests(ServiceBusEmulatorFixture serviceBus)
{
    /// <summary>Also referenced by <see cref="ConfigJsonDriftGuardTests"/> to keep the endpoint name in one place.</summary>
    public const string EndpointName = "it-roundtrip";

    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(30);

    private ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.AzureServiceBus;
            options.ConnectionString = serviceBus.ConnectionString;
            options.EndpointName = EndpointName;
            options.Assemblies.Add(typeof(RoundTripEvent).Assembly);
            // The emulator has no ServiceBusAdministrationClient: topology comes from the
            // checked-in Config.json instead of being declared at startup.
            options.AutoProvision = false;
        });
        services.AddModulusAzureServiceBusTransport();
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
            if (hostedService.GetType().Name != "OutboxProcessor")
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
    public async Task Publish_RoundTripsThroughEmulator_ToHandler()
    {
        RoundTripHandler.Handled.Clear();
        await using var host = await StartHost(BuildServices());

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
}
