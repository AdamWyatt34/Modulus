using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Testing.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

// Full AddModulusMessaging -> AddModulusTestTransport pipeline, driven through DI exactly as a
// host would wire it — the same shape as Modulus.Messaging.Tests.MessageBusTests, but asserting
// against TestMessageTransport's value-add (Published/DeadLettered/PublishFailure) instead of
// raw DbContext queries.
public class AddModulusTestTransportTests
{
    private static ServiceCollection BuildServices(Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderPlacedEvent).Assembly);
            configure?.Invoke(options);
        });
        services.AddModulusTestTransport();

        return services;
    }

    [Fact]
    public void AddModulusTestTransport_WithoutAddModulusMessaging_Throws()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(() => services.AddModulusTestTransport());
    }

    [Fact]
    public async Task Publish_DeliversTheEventToTheHandler_AndTheTestTransportRecordsIt()
    {
        var services = BuildServices();
        var handler = new RecordingOrderPlacedHandler();
        services.AddSingleton<IIntegrationEventHandler<TestOrderPlacedEvent>>(handler);

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);
        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var published = new TestOrderPlacedEvent { OrderId = 42, CustomerName = "Ada Lovelace" };
        await messageBus.Publish(published);

        await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);

        handler.HandledEvents[0].OrderId.ShouldBe(42);
        harness.Transport.Published.ShouldContain(e => e.MessageId == published.EventId);
        harness.Transport.PublishedEventsOf<TestOrderPlacedEvent>()
            .ShouldContain(e => e.CustomerName == "Ada Lovelace");
    }

    [Fact]
    public async Task PermanentlyFailingHandler_DeadLettersOnTheTestTransport_AfterExhaustingRetries()
    {
        var services = BuildServices(options =>
        {
            options.ConsumerRetry = new RetryPolicyOptions
            {
                MaxAttempts = 2,
                InitialInterval = TimeSpan.Zero,
                MaxInterval = TimeSpan.Zero,
                IntervalIncrement = TimeSpan.Zero,
            };
        });
        // Always fails: Attempts never exceeds int.MaxValue, so every attempt throws.
        var alwaysFails = new FlakyOrderPlacedHandler(failuresBeforeSuccess: int.MaxValue);
        services.AddSingleton<IIntegrationEventHandler<TestOrderPlacedEvent>>(alwaysFails);

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);
        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var published = new TestOrderPlacedEvent { OrderId = 1, CustomerName = "Poison" };
        await messageBus.Publish(published);

        await TestWait.WaitForConditionAsync(() => harness.Transport.DeadLettered.Count == 1);

        harness.Transport.DeadLetteredEventsOf<TestOrderPlacedEvent>()
            .ShouldContain(e => e.OrderId == 1);
    }

    [Fact]
    public async Task PublishScheduled_DeliversAfterTheEnqueueTime_ThroughTheFullPipeline()
    {
        var services = BuildServices();
        var handler = new RecordingOrderPlacedHandler();
        services.AddSingleton<IIntegrationEventHandler<TestOrderPlacedEvent>>(handler);

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);
        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.PublishScheduled(
            new TestOrderPlacedEvent { OrderId = 9, CustomerName = "Later" },
            DateTimeOffset.UtcNow.AddMilliseconds(300));

        await Task.Delay(100);
        handler.HandledEvents.ShouldBeEmpty();

        await TestWait.WaitForConditionAsync(() => handler.HandledEvents.Count == 1);
    }

    [Fact]
    public async Task PublishFailure_Injected_PropagatesFromMessageBusPublish()
    {
        var services = BuildServices();

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);
        using var scope = harness.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        harness.Transport.PublishFailure = new InvalidOperationException("simulated broker outage");

        await Should.ThrowAsync<InvalidOperationException>(
            () => messageBus.Publish(new TestOrderPlacedEvent { OrderId = 2, CustomerName = "Boom" }));
    }
}
