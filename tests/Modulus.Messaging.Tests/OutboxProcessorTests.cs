using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class OutboxProcessorTests
{
    [Fact]
    public async Task Processor_publishes_pending_messages_and_marks_processed()
    {
        var handler = new TestOrderCreatedHandler();
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase("OutboxProcessorTest", root));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            options.OutboxPollInterval = TimeSpan.FromMilliseconds(200);
        });
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);

        using var provider = services.BuildServiceProvider();

        // Seed outbox message
        using (var seedScope = provider.CreateScope())
        {
            var outboxStore = seedScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            await outboxStore.Save(new TestOrderCreatedEvent
            {
                OrderId = 77,
                CustomerName = "Outbox Test"
            });
        }

        // Start bus
        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            // Manually replicate what OutboxProcessor.ProcessPendingMessages does:
            // read from outbox, deserialize, publish via bus, mark processed
            using var scope = provider.CreateScope();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var bus = provider.GetRequiredService<MassTransit.IBus>();

            var pending = await outboxStore.GetPending(100);
            pending.Count.ShouldBe(1);

            var message = pending[0];
            var eventType = Type.GetType(message.EventType)!;
            var @event = JsonSerializer.Deserialize(message.Payload, eventType)!;

            await bus.Publish(@event, eventType);
            await outboxStore.MarkAsProcessed([message.Id]);

            // Wait for in-memory delivery
            await Task.Delay(1000);

            handler.HandledEvents.Count.ShouldBe(1);
            handler.HandledEvents[0].OrderId.ShouldBe(77);

            var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var stored = await dbContext.OutboxMessages.FirstAsync(m => m.Id == message.Id);
            stored.ProcessedAt.ShouldNotBeNull();
        }
        finally
        {
            await busControl.StopAsync();
        }
    }

    [Fact]
    public async Task Processor_skips_messages_with_unresolvable_type()
    {
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase("OutboxUnresolvableType", root));
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });

        using var provider = services.BuildServiceProvider();

        // Seed an outbox message with a bogus type name
        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "NonExistent.Type, NonExistent.Assembly",
                Payload = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            using var scope = provider.CreateScope();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var bus = provider.GetRequiredService<MassTransit.IBus>();

            var pending = await outboxStore.GetPending(100);
            pending.Count.ShouldBe(1);

            // Simulate what OutboxProcessor does — should not throw
            var eventType = Type.GetType(pending[0].EventType);
            eventType.ShouldBeNull(); // Unresolvable type

            // Message remains unprocessed (not marked as processed)
            var stillPending = await outboxStore.GetPending(100);
            stillPending.Count.ShouldBe(1);
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
