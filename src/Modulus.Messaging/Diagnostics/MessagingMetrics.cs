using System.Diagnostics.Metrics;

namespace Modulus.Messaging.Diagnostics;

/// <summary>
/// The <c>Modulus.Messaging</c> meter: outbox dispatch outcomes, consumer handler durations,
/// inbox deduplication, retries, and dead-letters. Resolved leniently — when no
/// <see cref="IMeterFactory"/> is registered the meter is created directly, so messaging
/// works unchanged in hosts without metrics DI and OpenTelemetry picks the instruments up
/// via <c>AddMeter("Modulus.Messaging")</c> either way.
/// </summary>
internal sealed class MessagingMetrics
{
    /// <summary>The meter name to subscribe to in OpenTelemetry configuration.</summary>
    public const string MeterName = "Modulus.Messaging";

    private readonly Counter<long> _outboxMessages;
    private readonly Counter<long> _outboxWakeups;
    private readonly Histogram<double> _handlerDuration;
    private readonly Counter<long> _inboxDeduplicated;
    private readonly Counter<long> _consumerRetries;
    private readonly Counter<long> _consumerDeadLettered;

    /// <summary>The owning meter instance — lets tests scope a listener to this instance
    /// rather than every same-named meter in the process.</summary>
    internal Meter Meter { get; }

    public MessagingMetrics(IMeterFactory? meterFactory)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        Meter = meter;

        _outboxMessages = meter.CreateCounter<long>(
            "modulus.messaging.outbox.messages",
            unit: "{message}",
            description: "Outbox dispatch attempts by outcome.");

        _outboxWakeups = meter.CreateCounter<long>(
            "modulus.messaging.outbox.wakeups",
            unit: "{wakeup}",
            description: "Outbox processor wake-ups by reason.");

        _handlerDuration = meter.CreateHistogram<double>(
            "modulus.messaging.consumer.handler.duration",
            unit: "ms",
            description: "Integration event handler execution time.");

        _inboxDeduplicated = meter.CreateCounter<long>(
            "modulus.messaging.inbox.deduplicated",
            unit: "{message}",
            description: "Handler executions skipped because the inbox already processed the pair.");

        _consumerRetries = meter.CreateCounter<long>(
            "modulus.messaging.consumer.retries",
            unit: "{attempt}",
            description: "In-process consumer retry attempts.");

        _consumerDeadLettered = meter.CreateCounter<long>(
            "modulus.messaging.consumer.dead_lettered",
            unit: "{message}",
            description: "Messages handed back to the transport for dead-lettering.");
    }

    /// <summary>Outcomes: published, skipped_unknown_type, deserialize_failed, retry_pending, dead_lettered.</summary>
    public void OutboxMessage(string outcome)
        => _outboxMessages.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Reasons: signal (woken by a notify), poll (interval elapsed), backlog (full batch — immediate re-dispatch).</summary>
    public void OutboxWakeup(string reason)
        => _outboxWakeups.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void HandlerDuration(double milliseconds, string handler, string outcome)
        => _handlerDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("handler", handler),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void InboxDeduplicated(string handler)
        => _inboxDeduplicated.Add(1, new KeyValuePair<string, object?>("handler", handler));

    public void ConsumerRetry(string messageType)
        => _consumerRetries.Add(1, new KeyValuePair<string, object?>("message_type", messageType));

    public void ConsumerDeadLettered(string messageType)
        => _consumerDeadLettered.Add(1, new KeyValuePair<string, object?>("message_type", messageType));
}
