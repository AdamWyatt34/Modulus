using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class MassTransitMessageBusTests
{
    [Fact]
    public async Task Publish_delivers_event_to_handler()
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

            var @event = new TestOrderCreatedEvent
            {
                OrderId = 42,
                CustomerName = "Test Customer"
            };

            await messageBus.Publish(@event);

            // Allow time for in-memory delivery
            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(1);
            handler.HandledEvents[0].OrderId.ShouldBe(42);
            handler.HandledEvents[0].CustomerName.ShouldBe("Test Customer");
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
