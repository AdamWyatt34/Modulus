using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class ConsumerAdapterTests
{
    [Fact]
    public async Task Consumer_adapter_delegates_to_integration_event_handler()
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

            await messageBus.Publish(new TestOrderCreatedEvent
            {
                OrderId = 99,
                CustomerName = "Adapter Test"
            });

            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(1);
            handler.HandledEvents[0].OrderId.ShouldBe(99);
            handler.HandledEvents[0].CustomerName.ShouldBe("Adapter Test");
        }
        finally
        {
            await busControl.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_adapter_passes_all_event_properties()
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

            var eventId = Guid.NewGuid();
            await messageBus.Publish(new TestOrderCreatedEvent
            {
                EventId = eventId,
                OrderId = 5,
                CustomerName = "Props Test",
                CorrelationId = "corr-123"
            });

            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(1);
            handler.HandledEvents[0].EventId.ShouldBe(eventId);
            handler.HandledEvents[0].CorrelationId.ShouldBe("corr-123");
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
