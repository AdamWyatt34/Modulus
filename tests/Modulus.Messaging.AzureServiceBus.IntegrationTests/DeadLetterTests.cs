using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.AzureServiceBus.IntegrationTests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests;

[Collection(ServiceBusCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DeadLetterTests(ServiceBusEmulatorFixture serviceBus)
{
    /// <summary>Also referenced by <see cref="ConfigJsonDriftGuardTests"/> to keep the endpoint name in one place.</summary>
    public const string EndpointName = "it-dlx";

    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(30);

    private ServiceCollection BuildServices(Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.AzureServiceBus;
            options.ConnectionString = serviceBus.ConnectionString;
            options.EndpointName = EndpointName;
            options.Assemblies.Add(typeof(DeadLetterEvent).Assembly);
            options.AutoProvision = false;
            configure?.Invoke(options);
        });
        services.AddModulusAzureServiceBusTransport();
        return services;
    }

    private static async Task<StartedHost> StartHost(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var started = new List<IHostedService>();

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
    public async Task FailingHandler_ExhaustsRetries_MessageLandsInDeadLetterSubQueue()
    {
        DeadLetterHandler.Reset();
        await using var host = await StartHost(BuildServices(options =>
        {
            options.ConsumerRetry.MaxAttempts = 2;
            options.ConsumerRetry.InitialInterval = TimeSpan.Zero;
            options.ConsumerRetry.MaxInterval = TimeSpan.Zero;
            options.ConsumerRetry.IntervalIncrement = TimeSpan.Zero;
        }));

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.Publish(new DeadLetterEvent { Value = 13 });

        var topic = AzureServiceBusTopology.TopicName(
            Serialization.MessageTypeRegistry.GetStableName(typeof(DeadLetterEvent)));
        var subscription = AzureServiceBusTopology.SubscriptionName(EndpointName);

        await using var client = new ServiceBusClient(serviceBus.ConnectionString);
        await using var deadLetterReceiver = client.CreateReceiver(topic, subscription, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
        });

        ServiceBusReceivedMessage? deadLettered = null;
        await WaitFor(
            () =>
            {
                deadLettered = deadLetterReceiver.ReceiveMessageAsync(TimeSpan.FromMilliseconds(500))
                    .GetAwaiter().GetResult();
                return deadLettered is not null;
            },
            "message should land in the subscription's dead-letter sub-queue after retries are exhausted");

        DeadLetterHandler.Attempts.ShouldBe(2);
        deadLettered!.Subject.ShouldBe(typeof(DeadLetterEvent).FullName);
    }
}
