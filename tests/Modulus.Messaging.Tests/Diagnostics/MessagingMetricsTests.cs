using System.Diagnostics.Metrics;
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

namespace Modulus.Messaging.Tests.Diagnostics;

public class MessagingMetricsTests
{
    /// <summary>
    /// Collects measurements from one specific <see cref="MessagingMetrics"/> instance —
    /// scoping by meter instance (not name) keeps parallel test classes from bleeding in.
    /// </summary>
    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, object Value, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly System.Threading.Lock _sync = new();

        public MeterCapture(MessagingMetrics metrics)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, metrics.Meter))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var tagMap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
                tagMap[tag.Key] = tag.Value;

            lock (_sync)
            {
                _measurements.Add((instrument.Name, value!, tagMap));
            }
        }

        public List<(string Instrument, object Value, Dictionary<string, object?> Tags)> For(string instrument)
        {
            lock (_sync)
            {
                return _measurements.Where(m => m.Instrument == instrument).ToList();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private static ConsumerDispatcher BuildDispatcher(
        IServiceCollection services,
        MessagingMetrics metrics,
        int maxAttempts = 1)
    {
        var provider = services.BuildServiceProvider();
        return new ConsumerDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]),
            NullLogger<ConsumerDispatcher>.Instance,
            new MessagingOptions
            {
                ConsumerRetry = new RetryPolicyOptions
                {
                    MaxAttempts = maxAttempts,
                    InitialInterval = TimeSpan.Zero,
                    MaxInterval = TimeSpan.Zero,
                    IntervalIncrement = TimeSpan.Zero,
                },
            },
            metrics);
    }

    private static TransportEnvelope EnvelopeFor(TestOrderCreatedEvent @event) => new(
        MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
        @event.EventId,
        @event.CorrelationId,
        @event.OccurredOn,
        MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent)));

    [Fact]
    public async Task SuccessfulDispatch_RecordsHandlerDurationWithSuccessOutcome()
    {
        var metrics = new MessagingMetrics(meterFactory: null);
        using var capture = new MeterCapture(metrics);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(new TestOrderCreatedHandler());
        var dispatcher = BuildDispatcher(services, metrics);

        await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "M" }),
            CancellationToken.None);

        var durations = capture.For("modulus.messaging.consumer.handler.duration");
        durations.Count.ShouldBe(1);
        durations[0].Tags["handler"].ShouldBe(nameof(TestOrderCreatedHandler));
        durations[0].Tags["outcome"].ShouldBe("success");
    }

    [Fact]
    public async Task FailingDispatch_RecordsFailureDurationRetriesAndDeadLetter()
    {
        var metrics = new MessagingMetrics(meterFactory: null);
        using var capture = new MeterCapture(metrics);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(
            new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue));
        var dispatcher = BuildDispatcher(services, metrics, maxAttempts: 3);

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "F" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        capture.For("modulus.messaging.consumer.handler.duration")
            .ShouldAllBe(m => (string)m.Tags["outcome"]! == "failure");
        capture.For("modulus.messaging.consumer.retries").Count.ShouldBe(2);
        capture.For("modulus.messaging.consumer.dead_lettered").Count.ShouldBe(1);
    }

    [Fact]
    public async Task DuplicateDelivery_RecordsInboxDeduplication()
    {
        var metrics = new MessagingMetrics(meterFactory: null);
        using var capture = new MeterCapture(metrics);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(new TestOrderCreatedHandler());
        services.AddSingleton<IInboxStore>(new FakeInboxStore());
        var dispatcher = BuildDispatcher(services, metrics);

        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 3, CustomerName = "D" });
        await dispatcher.DispatchAsync(envelope, CancellationToken.None);
        await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        var dedup = capture.For("modulus.messaging.inbox.deduplicated");
        dedup.Count.ShouldBe(1);
        dedup[0].Tags["handler"].ShouldBe(nameof(TestOrderCreatedHandler));
    }

    [Fact]
    public void OutboxCounter_TagsOutcome()
    {
        var metrics = new MessagingMetrics(meterFactory: null);
        using var capture = new MeterCapture(metrics);

        metrics.OutboxMessage("published");
        metrics.OutboxMessage("dead_lettered");

        var outcomes = capture.For("modulus.messaging.outbox.messages")
            .Select(m => (string)m.Tags["outcome"]!)
            .ToList();
        outcomes.ShouldBe(["published", "dead_lettered"]);
    }
}
