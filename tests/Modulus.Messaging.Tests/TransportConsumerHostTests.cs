using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

// Drives TransportConsumerHost directly against a FakeMessageTransport — no real broker,
// no BackgroundService lifetime for the outbox side.
public sealed class TransportConsumerHostTests
{
    private static ConsumerDispatcher BuildDispatcher(IServiceCollection services) =>
        new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]),
            NullLogger<ConsumerDispatcher>.Instance,
            new MessagingOptions(),
            new MessagingMetrics(meterFactory: null));

    private static TransportSubscriptionCatalog CatalogFor(params TransportSubscription[] subscriptions) =>
        new(subscriptions);

    [Fact]
    public async Task StartAsync_EmptyCatalog_DoesNotStartConsuming()
    {
        var transport = new FakeMessageTransport();
        var services = new ServiceCollection();
        var dispatcher = BuildDispatcher(services);
        var host = new TransportConsumerHost(
            transport,
            dispatcher,
            CatalogFor(),
            NullLogger<TransportConsumerHost>.Instance);

        await host.StartAsync(CancellationToken.None);

        transport.StartConsumingCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task StopAsync_EmptyCatalog_IsSafeNoOp()
    {
        var transport = new FakeMessageTransport();
        var services = new ServiceCollection();
        var dispatcher = BuildDispatcher(services);
        var host = new TransportConsumerHost(
            transport,
            dispatcher,
            CatalogFor(),
            NullLogger<TransportConsumerHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        transport.StopConsumingCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task StartAsync_NonEmptyCatalog_StartsConsumingWithCatalogSubscriptions()
    {
        var transport = new FakeMessageTransport();
        var services = new ServiceCollection();
        var dispatcher = BuildDispatcher(services);
        var subscription = new TransportSubscription(
            typeof(TestOrderCreatedEvent),
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)));
        var host = new TransportConsumerHost(
            transport,
            dispatcher,
            CatalogFor(subscription),
            NullLogger<TransportConsumerHost>.Instance);

        await host.StartAsync(CancellationToken.None);

        transport.StartConsumingCallCount.ShouldBe(1);
        transport.LastSubscriptions.ShouldBe([subscription]);
        transport.CapturedCallback.ShouldNotBeNull();
    }

    [Fact]
    public async Task StartAsync_NonEmptyCatalog_CapturedCallbackForwardsToDispatcherAndInvokesHandler()
    {
        var handler = new TestOrderCreatedHandler();
        var transport = new FakeMessageTransport();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services);
        var subscription = new TransportSubscription(
            typeof(TestOrderCreatedEvent),
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)));
        var host = new TransportConsumerHost(
            transport,
            dispatcher,
            CatalogFor(subscription),
            NullLogger<TransportConsumerHost>.Instance);

        await host.StartAsync(CancellationToken.None);

        var @event = new TestOrderCreatedEvent { OrderId = 42, CustomerName = "Callback" };
        var envelope = new TransportEnvelope(
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
            @event.EventId,
            @event.CorrelationId,
            @event.OccurredOn,
            MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent)));

        var result = await transport.CapturedCallback!(envelope, CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
        handler.HandledEvents[0].OrderId.ShouldBe(42);
    }

    [Fact]
    public async Task StopAsync_NonEmptyCatalog_StopsConsuming()
    {
        var transport = new FakeMessageTransport();
        var services = new ServiceCollection();
        var dispatcher = BuildDispatcher(services);
        var subscription = new TransportSubscription(
            typeof(TestOrderCreatedEvent),
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)));
        var host = new TransportConsumerHost(
            transport,
            dispatcher,
            CatalogFor(subscription),
            NullLogger<TransportConsumerHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        transport.StopConsumingCallCount.ShouldBe(1);
    }
}
