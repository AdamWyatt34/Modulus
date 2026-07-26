using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging;
using Modulus.Messaging.Outbox;
using Modulus.Testing.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

public class ModulusMessagingTestHarnessTests
{
    [Fact]
    public async Task StartAsync_StartsHostedServicesInRegistrationOrder_AndStopsInReverseOrder()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IHostedService>(new RecordingHostedService("A", log));
        services.AddSingleton<IHostedService>(new RecordingHostedService("B", log));
        services.AddSingleton<IHostedService>(new RecordingHostedService("C", log));

        var harness = await ModulusMessagingTestHarness.StartAsync(services);
        log.ShouldBe(["start:A", "start:B", "start:C"]);

        await harness.DisposeAsync();
        log.ShouldBe(["start:A", "start:B", "start:C", "stop:C", "stop:B", "stop:A"]);
    }

    [Fact]
    public async Task AddModulusMessaging_RegistersTheConsumerHostBeforeTheOutboxProcessor()
    {
        // TransportConsumerHost and OutboxProcessor are internal to Modulus.Messaging, so this
        // asserts on the runtime type name rather than the type itself — the registration order
        // AddModulusMessagingCore relies on (consumer host subscribed before the outbox
        // processor's first dispatch pass; stopped in reverse so consumers drain after the
        // outbox stops publishing).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderPlacedEvent).Assembly);
        });
        services.AddModulusTestTransport();

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);

        var hostedServiceNames = harness.Provider.GetServices<IHostedService>()
            .Select(service => service.GetType().Name)
            .ToList();

        var consumerHostIndex = hostedServiceNames.IndexOf("TransportConsumerHost");
        var outboxProcessorIndex = hostedServiceNames.IndexOf("OutboxProcessor");

        consumerHostIndex.ShouldBeGreaterThanOrEqualTo(0);
        outboxProcessorIndex.ShouldBeGreaterThanOrEqualTo(0);
        consumerHostIndex.ShouldBeLessThan(outboxProcessorIndex);
    }

    [Fact]
    public async Task Transport_ResolvesTheTestMessageTransportRegisteredByAddModulusTestTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderPlacedEvent).Assembly);
        });
        services.AddModulusTestTransport();

        await using var harness = await ModulusMessagingTestHarness.StartAsync(services);

        harness.Transport.ShouldNotBeNull();
        harness.Transport.ShouldBeSameAs(harness.Provider.GetRequiredService<TestMessageTransport>());
    }
}
