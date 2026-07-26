using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Modulus.Messaging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Transports;

namespace Modulus.Testing;

/// <summary>
/// A public, DI-swappable stand-in for the in-memory transport shipped inside
/// <c>Modulus.Messaging</c> (which is <see langword="internal"/> and cannot be resolved or
/// replaced by name). Built entirely against the public transport SPI —
/// <see cref="IMessageTransport"/>, <see cref="TransportEnvelope"/>,
/// <see cref="TransportSubscription"/>, <see cref="MessageDispatchResult"/> — no internals
/// access to <c>Modulus.Messaging</c> is required or used.
/// </summary>
/// <remarks>
/// <para>
/// Delivery semantics deliberately mirror the library's internal in-memory transport: one
/// unbounded channel per subscribed event type, <see cref="TransportEnvelope.ScheduledEnqueueTimeUtc"/>
/// honored with an in-process timer (the schedule is cleared on the delivered copy so a later
/// redelivery is not re-delayed), and <see cref="MessageDispatchResult.Retry"/> honored by
/// rescheduling a copy with an incremented delivery-attempt header — computed with the same
/// exponential backoff formula the library uses for <c>ConsumerRetryMode.Broker</c>. Because the
/// library's header names and backoff calculator are <see langword="internal"/>, both are
/// reimplemented here against the same constants and formula (see <see cref="DeliveryAttemptHeader"/>
/// and the private <c>GetRetryDelay</c> helper) so a test written against
/// <c>ConsumerRetryMode.Broker</c> observes the same attempt header a real broker transport
/// would produce.
/// </para>
/// <para>
/// On top of that parity, this transport adds the observability a test needs and the
/// production in-memory transport does not provide: every published envelope is recorded
/// (<see cref="Published"/>), envelopes the consumer pipeline dead-letters are kept instead of
/// logged-and-dropped (<see cref="DeadLettered"/>), and publish failures can be injected
/// (<see cref="PublishFailure"/>).
/// </para>
/// </remarks>
public sealed class TestMessageTransport(MessagingOptions? options = null) : IMessageTransport, ITransportHealthProbe
{
    /// <summary>
    /// The delivery-attempt header name used for <see cref="MessageDispatchResult.Retry"/>
    /// redelivery copies. Matches <c>Modulus.Messaging.Dispatch.RedeliveryHeaders.AttemptHeader</c>
    /// exactly (that type is internal), so a captured envelope's <see cref="TransportEnvelope.Headers"/>
    /// reads the same value a shipped transport would produce.
    /// </summary>
    public const string DeliveryAttemptHeader = "modulus-delivery-attempt";

    private readonly MessagingOptions _options = options ?? new MessagingOptions();
    private readonly ConcurrentDictionary<string, Channel<TransportEnvelope>> _channels = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<TransportEnvelope> _published = [];
    private readonly ConcurrentQueue<TransportEnvelope> _deadLettered = [];
    private readonly List<Task> _readerLoops = [];
    private CancellationTokenSource? _stopSource;

    /// <summary>
    /// When set, every subsequent call to <see cref="PublishAsync"/> throws this exception
    /// instead of publishing — inject to test a caller's failure handling. Reset to
    /// <see langword="null"/> to resume normal publishing.
    /// </summary>
    public Exception? PublishFailure { get; set; }

    /// <summary>
    /// A snapshot of every envelope passed to <see cref="PublishAsync"/>, in publish order,
    /// regardless of whether anything was subscribed to receive it. Safe to enumerate while
    /// publishes are still happening on another thread — it is a point-in-time copy, not a live
    /// view.
    /// </summary>
    public IReadOnlyList<TransportEnvelope> Published => [.. _published];

    /// <summary>
    /// A snapshot of every envelope the consumer pipeline dead-lettered (all delivery attempts
    /// exhausted, or the pipeline reported <see cref="MessageDispatchResult.DeadLetter"/> for any
    /// other reason). The production in-memory transport only logs and drops these; this
    /// transport keeps them so a test can assert a poison message actually reached
    /// dead-letter status.
    /// </summary>
    public IReadOnlyList<TransportEnvelope> DeadLettered => [.. _deadLettered];

    /// <summary>
    /// Deserializes <see cref="Published"/> envelopes whose <see cref="TransportEnvelope.MessageType"/>
    /// matches <typeparamref name="TEvent"/>'s stable wire name (its <see cref="Type.FullName"/>)
    /// back into <typeparamref name="TEvent"/> instances, using <see cref="System.Text.Json"/>
    /// with default options — the same serialization <c>Modulus.Messaging</c> uses on the wire.
    /// </summary>
    public IReadOnlyList<TEvent> PublishedEventsOf<TEvent>() where TEvent : IIntegrationEvent
        => DeserializeMatching<TEvent>(Published);

    /// <summary>
    /// Deserializes <see cref="DeadLettered"/> envelopes whose <see cref="TransportEnvelope.MessageType"/>
    /// matches <typeparamref name="TEvent"/>'s stable wire name back into <typeparamref name="TEvent"/>
    /// instances. See <see cref="PublishedEventsOf{TEvent}"/> for the matching/deserialization rules.
    /// </summary>
    public IReadOnlyList<TEvent> DeadLetteredEventsOf<TEvent>() where TEvent : IIntegrationEvent
        => DeserializeMatching<TEvent>(DeadLettered);

    private static IReadOnlyList<TEvent> DeserializeMatching<TEvent>(IReadOnlyList<TransportEnvelope> envelopes)
        where TEvent : IIntegrationEvent
    {
        var wireName = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        var matches = new List<TEvent>();

        foreach (var envelope in envelopes)
        {
            if (!string.Equals(envelope.MessageType, wireName, StringComparison.Ordinal))
                continue;

            var deserialized = JsonSerializer.Deserialize<TEvent>(envelope.Body.Span);
            if (deserialized is not null)
                matches.Add(deserialized);
        }

        return matches;
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (PublishFailure is { } failure)
            throw failure;

        _published.Enqueue(envelope);

        if (!_channels.TryGetValue(envelope.MessageType, out var channel))
            return;

        var delay = envelope.ScheduledEnqueueTimeUtc is { } enqueueAt
            ? enqueueAt - DateTimeOffset.UtcNow
            : TimeSpan.Zero;

        if (delay <= TimeSpan.Zero)
        {
            await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Clear the schedule on the delivered copy so a Retry redelivery does not re-delay it.
        EnqueueAfter(channel, envelope with { ScheduledEnqueueTimeUtc = null }, delay);
    }

    // Fire-and-forget by design: TryWrite on an unbounded channel only fails once the writer
    // completes (stop), at which point the copy is deliberately lost — this transport has no
    // durability guarantee, matching the library's internal in-memory transport.
    private static void EnqueueAfter(Channel<TransportEnvelope> channel, TransportEnvelope envelope, TimeSpan delay)
        => _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            channel.Writer.TryWrite(envelope);
        });

    /// <inheritdoc />
    public Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default)
    {
        _stopSource = new CancellationTokenSource();

        foreach (var subscription in subscriptions)
        {
            var channel = _channels.GetOrAdd(
                subscription.MessageTypeName,
                static _ => Channel.CreateUnbounded<TransportEnvelope>());

            _readerLoops.Add(Task.Run(
                () => ReadLoop(channel, onMessage, _stopSource.Token),
                CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoop(
        Channel<TransportEnvelope> channel,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken stopToken)
    {
        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await onMessage(envelope, stopToken).ConfigureAwait(false);

                    if (result == MessageDispatchResult.DeadLetter)
                        _deadLettered.Enqueue(envelope);
                    else if (result == MessageDispatchResult.Retry)
                        ScheduleRedelivery(channel, envelope);
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Swallowed deliberately, matching the internal in-memory transport: an
                    // unexpected dispatch failure must not take down this channel's read loop
                    // for every other message still queued behind it.
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Forced stop after the drain window elapsed.
        }
    }

    private void ScheduleRedelivery(Channel<TransportEnvelope> channel, TransportEnvelope envelope)
    {
        var attempt = GetDeliveryAttempt(envelope);
        var delay = GetRetryDelay(_options.ConsumerRetry, attempt);
        var copy = envelope with { Headers = ForRedelivery(envelope) };

        if (delay <= TimeSpan.Zero)
        {
            channel.Writer.TryWrite(copy);
            return;
        }

        EnqueueAfter(channel, copy, delay);
    }

    /// <inheritdoc />
    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        // Pending delay timers are deliberately not awaited — a long backoff must not hold
        // shutdown hostage. Their TryWrite no-ops against the completed writers below, so any
        // undelivered copies are simply lost, matching this transport's no-durability contract.
        foreach (var channel in _channels.Values)
            channel.Writer.TryComplete();

        try
        {
            // Completed writers let the reader loops drain buffered messages and exit.
            await Task.WhenAll(_readerLoops).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain window elapsed: force the loops down.
            if (_stopSource is not null)
                await _stopSource.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            _readerLoops.Clear();
            _channels.Clear();
            _stopSource?.Dispose();
            _stopSource = null;
        }
    }

    /// <inheritdoc />
    public ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TransportHealth(true, "Test transport has no broker."));

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopConsumingAsync().ConfigureAwait(false);

    // --- Reimplementation of Modulus.Messaging.Dispatch.RedeliveryHeaders / RetryDelayCalculator ---
    // Both are internal to Modulus.Messaging, so this transport (built only against the public
    // SPI) reimplements the same header name and formula rather than gaining internals access.

    private static int GetDeliveryAttempt(TransportEnvelope envelope)
        => envelope.Headers is { } headers
            && headers.TryGetValue(DeliveryAttemptHeader, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt)
            && attempt >= 1
                ? attempt
                : 1;

    private static IReadOnlyDictionary<string, string> ForRedelivery(TransportEnvelope envelope)
    {
        var headers = envelope.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal);

        headers[DeliveryAttemptHeader] = (GetDeliveryAttempt(envelope) + 1).ToString(CultureInfo.InvariantCulture);
        return headers;
    }

    // delay(n) = min(MaxInterval, InitialInterval + IntervalIncrement * (2^(n-1) - 1)) for
    // retry number n (1-based) — the same exponential-backoff formula as
    // Modulus.Messaging.Dispatch.RetryDelayCalculator.
    private static TimeSpan GetRetryDelay(RetryPolicyOptions policy, int retryNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);

        var exponent = Math.Min(retryNumber - 1, 30);
        var incrementMs = policy.IntervalIncrement.TotalMilliseconds * (Math.Pow(2, exponent) - 1);
        var delayMs = policy.InitialInterval.TotalMilliseconds + incrementMs;

        return delayMs >= policy.MaxInterval.TotalMilliseconds
            ? policy.MaxInterval
            : TimeSpan.FromMilliseconds(delayMs);
    }
}
