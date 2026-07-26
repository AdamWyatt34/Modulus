using System.Diagnostics;
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

namespace Modulus.Messaging.Tests.Dispatch;

public sealed class ConsumerDispatcherTracingTests : IDisposable
{
    private readonly List<Activity> _completed = [];
    private readonly ActivityListener _listener;

    public ConsumerDispatcherTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (_completed) _completed.Add(activity); },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static ConsumerDispatcher BuildDispatcher(IServiceCollection services)
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
                    MaxAttempts = 1,
                    InitialInterval = TimeSpan.Zero,
                    MaxInterval = TimeSpan.Zero,
                    IntervalIncrement = TimeSpan.Zero,
                },
            },
            new MessagingMetrics(meterFactory: null));
    }

    private static TransportEnvelope EnvelopeFor(
        TestOrderCreatedEvent @event,
        IReadOnlyDictionary<string, string>? headers = null) => new(
        MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
        @event.EventId,
        @event.CorrelationId,
        @event.OccurredOn,
        MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent)))
    {
        Headers = headers,
    };

    // The listener is process-global while test classes run in parallel, so other classes'
    // spans from the same source land in _completed too — select this test's by message id.
    private Activity CompletedConsumeActivity(Guid messageId)
    {
        lock (_completed)
        {
            return _completed
                .Where(a => Equals(a.GetTagItem("modulus.message_id"), messageId))
                .ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task Dispatch_with_traceparent_header_parents_the_consumer_activity_on_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(new TestOrderCreatedHandler());
        var dispatcher = BuildDispatcher(services);

        var remoteTraceId = ActivityTraceId.CreateRandom();
        var remoteSpanId = ActivitySpanId.CreateRandom();
        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "T" },
            new Dictionary<string, string>
            {
                ["traceparent"] = $"00-{remoteTraceId}-{remoteSpanId}-01",
            });

        var result = await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        var activity = CompletedConsumeActivity(envelope.MessageId);
        activity.Kind.ShouldBe(ActivityKind.Consumer);
        activity.TraceId.ShouldBe(remoteTraceId);
        activity.ParentSpanId.ShouldBe(remoteSpanId);
        activity.HasRemoteParent.ShouldBeTrue();
        activity.GetTagItem("modulus.message_id").ShouldBe(envelope.MessageId);
        activity.GetTagItem("modulus.outcome").ShouldBe("acknowledge");
    }

    [Fact]
    public async Task Dispatch_without_headers_starts_a_root_consumer_activity()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(new TestOrderCreatedHandler());
        var dispatcher = BuildDispatcher(services);

        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "T" });
        await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        var activity = CompletedConsumeActivity(envelope.MessageId);
        activity.Kind.ShouldBe(ActivityKind.Consumer);
        activity.Parent.ShouldBeNull();
        activity.DisplayName.ShouldEndWith(" process");
    }

    [Fact]
    public async Task Dispatch_that_dead_letters_marks_the_activity_as_error()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(
            new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue));
        var dispatcher = BuildDispatcher(services);

        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 3, CustomerName = "T" });
        var result = await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        var activity = CompletedConsumeActivity(envelope.MessageId);
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.GetTagItem("modulus.outcome").ShouldBe("dead_letter");
    }

    [Fact]
    public async Task Handler_sees_the_consumer_activity_as_ambient()
    {
        Activity? seen = null;
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(
            new CapturingHandler(() => seen = Activity.Current));
        var dispatcher = BuildDispatcher(services);

        await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 4, CustomerName = "T" }),
            CancellationToken.None);

        seen.ShouldNotBeNull();
        seen.Source.Name.ShouldBe(MessagingDiagnostics.ActivitySourceName);
    }

    // The null default keeps the type constructible by DI: handler discovery auto-registers
    // every IIntegrationEventHandler in this assembly for the DI-driven end-to-end tests.
    private sealed class CapturingHandler(Action? capture = null) : IIntegrationEventHandler<TestOrderCreatedEvent>
    {
        public Task Handle(TestOrderCreatedEvent @event, CancellationToken cancellationToken)
        {
            capture?.Invoke();
            return Task.CompletedTask;
        }
    }
}
