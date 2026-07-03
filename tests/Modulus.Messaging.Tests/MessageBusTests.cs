using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

// Full publish -> in-memory transport -> consumer pipeline -> handler roundtrips,
// driven through DI exactly as a host would wire it.
public class MessageBusTests
{
    private static ServiceCollection BuildServices(params object[] handlers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });

        foreach (var handler in handlers)
        {
            if (handler is IIntegrationEventHandler<TestOrderCreatedEvent> typed)
                services.AddSingleton(typed);
        }

        return services;
    }

    [Fact]
    public async Task Publish_DeliversEventToHandler()
    {
        var handler = new TestOrderCreatedHandler();
        await using var harness = await MessagingTestHarness.StartAsync(BuildServices(handler));

        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.Publish(new TestOrderCreatedEvent
        {
            OrderId = 42,
            CustomerName = "Test Customer"
        });

        await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);

        handler.HandledEvents[0].OrderId.ShouldBe(42);
        handler.HandledEvents[0].CustomerName.ShouldBe("Test Customer");
    }

    [Fact]
    public async Task Publish_PassesAllEventProperties()
    {
        var handler = new TestOrderCreatedHandler();
        await using var harness = await MessagingTestHarness.StartAsync(BuildServices(handler));

        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var eventId = Guid.NewGuid();
        await messageBus.Publish(new TestOrderCreatedEvent
        {
            EventId = eventId,
            OrderId = 5,
            CustomerName = "Props Test",
            CorrelationId = "corr-123"
        });

        await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);

        handler.HandledEvents[0].EventId.ShouldBe(eventId);
        handler.HandledEvents[0].CorrelationId.ShouldBe("corr-123");
    }

    [Fact]
    public async Task Publish_MultipleEvents_AllDelivered()
    {
        var handler = new TestOrderCreatedHandler();
        await using var harness = await MessagingTestHarness.StartAsync(BuildServices(handler));

        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.Publish(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "A" });
        await messageBus.Publish(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "B" });

        await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 2);
    }

    [Fact]
    public async Task Send_UnsupportedDestinationScheme_Throws()
    {
        var services = BuildServices();
        await using var harness = await MessagingTestHarness.StartAsync(services);

        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await Should.ThrowAsync<ArgumentException>(() =>
            messageBus.Send(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "X" }, new Uri("rabbitmq://host/q")));
    }
}
