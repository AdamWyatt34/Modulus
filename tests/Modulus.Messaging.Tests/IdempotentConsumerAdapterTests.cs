using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class IdempotentConsumerAdapterTests
{
    [Fact]
    public async Task Without_inbox_handler_receives_event()
    {
        // When IInboxStore is not registered, the IdempotentConsumerAdapter
        // falls through to direct handler execution (no idempotency check).
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        // IInboxStore is NOT registered — adapter should fall through
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);

        using var provider = services.BuildServiceProvider();
        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            using var scope = provider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await messageBus.Publish(new TestOrderCreatedEvent
            {
                OrderId = 1,
                CustomerName = "NoInbox"
            });

            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(1);
            handler.HandledEvents[0].OrderId.ShouldBe(1);
        }
        finally
        {
            await busControl.StopAsync();
        }
    }

    [Fact]
    public async Task Without_inbox_multiple_different_events_all_delivered()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);

        using var provider = services.BuildServiceProvider();
        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            using var scope = provider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await messageBus.Publish(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "A" });
            await messageBus.Publish(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "B" });

            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(2);
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
