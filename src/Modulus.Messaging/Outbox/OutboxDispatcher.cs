using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessageTransport transport,
    MessageTypeRegistry typeRegistry,
    ILogger<OutboxDispatcher> logger,
    MessagingOptions options,
    MessagingMetrics metrics) : IOutboxDispatcher
{
    /// <summary>The <see cref="ActivitySource"/> name to subscribe to in OpenTelemetry configuration.</summary>
    public const string ActivitySourceName = "Modulus.Messaging.Outbox";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    // A stable per-instance identity for the lifetime of this dispatcher (one per process,
    // registered as a singleton) — the "me" in ClaimPending's "unclaimed or claimed by me"
    // semantics. Machine name alone is not enough (multiple replicas can share a host, e.g.
    // containers or several instances of a Windows service); the GUID suffix makes it unique
    // even then. Generated once and reused for every dispatch pass this process runs, so
    // MarkAsProcessed/MarkAsFailed always match the claim ClaimPending just took.
    private readonly string _ownerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    // Strips ", Version=..." / ", Culture=..." / ", PublicKeyToken=..." segments from an
    // assembly-qualified name, including ones nested inside generic-argument brackets
    // ("[[...]]"), so a deploy that only bumps the assembly's version/culture/key does not
    // orphan rows an older build wrote (see NormalizeAssemblyQualifiedName).
    private static readonly Regex VersionInfoPattern = new(
        @",\s*(?:Version|Culture|PublicKeyToken)=[^,\]]*",
        RegexOptions.Compiled);

    // Keyed by AssemblyQualifiedName for compatibility with rows EfOutboxStore already wrote.
    // The normalized map is the version-insensitive fallback used when the exact AQN no
    // longer matches (see TryResolveEventType).
    private readonly (Dictionary<string, Type> Exact, Dictionary<string, Type> Normalized) _allowlist
        = BuildAllowlist(options.Assemblies);

    private static (Dictionary<string, Type> Exact, Dictionary<string, Type> Normalized) BuildAllowlist(
        IEnumerable<Assembly> assemblies)
    {
        var integrationEventType = typeof(IIntegrationEvent);
        var exact = new Dictionary<string, Type>(StringComparer.Ordinal);
        var normalized = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypesSafe())
            {
                if (type is { IsAbstract: false, IsInterface: false }
                    && integrationEventType.IsAssignableFrom(type))
                {
                    var assemblyQualifiedName = type.AssemblyQualifiedName;
                    if (assemblyQualifiedName is null)
                        continue;

                    exact.TryAdd(assemblyQualifiedName, type);
                    normalized.TryAdd(NormalizeAssemblyQualifiedName(assemblyQualifiedName), type);
                }
            }
        }

        return (exact, normalized);
    }

    /// <summary>Removes Version/Culture/PublicKeyToken segments from an assembly-qualified name.</summary>
    private static string NormalizeAssemblyQualifiedName(string assemblyQualifiedName)
        => VersionInfoPattern.Replace(assemblyQualifiedName, string.Empty);

    /// <summary>
    /// Resolves a stored <see cref="OutboxMessage.EventType"/> to an allow-listed CLR type.
    /// Matches the exact assembly-qualified name first (the common case) and falls back to a
    /// Version/Culture/PublicKeyToken-insensitive comparison, so a deploy that only bumps the
    /// assembly's version does not turn in-flight rows into permanent poison rows.
    /// </summary>
    private bool TryResolveEventType(string storedEventType, [NotNullWhen(true)] out Type? eventType)
    {
        if (_allowlist.Exact.TryGetValue(storedEventType, out eventType))
            return true;

        return _allowlist.Normalized.TryGetValue(NormalizeAssemblyQualifiedName(storedEventType), out eventType);
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var maxAttempts = options.RetryPolicy.MaxAttempts;
        var pending = await outboxStore
            .ClaimPending(_ownerId, options.OutboxClaimLease, options.OutboxBatchSize, maxAttempts, cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return 0;

        var processedIds = new List<Guid>();

        // Rows that made forward progress this pass: published, or durably marked failed (so
        // Attempts advanced and — via the NextAttemptOnUtc backoff written alongside it — the
        // row will not be refetched next pass). A row whose MarkAsFailed call itself throws
        // does not count: nothing changed for it in the store, so claiming progress would be a
        // lie and could mask a persistently broken store behind an artificially healthy count.
        var progressCount = 0;

        foreach (var message in pending)
        {
            using var activity = StartDispatchActivity(message);
            activity?.SetTag("modulus.message_id", message.Id);
            activity?.SetTag("modulus.event_type", message.EventType);

            var nextAttempt = message.Attempts + 1;
            var nextAttemptOnUtc = DateTime.UtcNow + RetryDelayCalculator.GetDelay(options.RetryPolicy, nextAttempt);

            if (!TryResolveEventType(message.EventType, out var eventType))
            {
                logger.LogWarning(
                    "Outbox message {MessageId} has unknown or disallowed event type {EventType}. Skipping.",
                    message.Id,
                    message.EventType);
                activity?.SetTag("modulus.outcome", "skipped_unknown_type");
                metrics.OutboxMessage("skipped_unknown_type");

                if (await TryMarkAsFailedAsync(
                        outboxStore,
                        message.Id,
                        $"Unknown or disallowed event type '{message.EventType}'.",
                        nextAttemptOnUtc,
                        cancellationToken).ConfigureAwait(false))
                {
                    progressCount++;
                }

                continue;
            }

            try
            {
                // Deserialization doubles as payload validation before the bytes go on the wire.
                var @event = JsonSerializer.Deserialize(message.Payload, eventType);
                if (@event is not IIntegrationEvent integrationEvent)
                {
                    logger.LogWarning(
                        "Failed to deserialize outbox message {MessageId}",
                        message.Id);
                    activity?.SetTag("modulus.outcome", "deserialize_failed");
                    metrics.OutboxMessage("deserialize_failed");

                    if (await TryMarkAsFailedAsync(
                            outboxStore,
                            message.Id,
                            $"Payload deserialized to null for event type '{eventType.AssemblyQualifiedName}'.",
                            nextAttemptOnUtc,
                            cancellationToken).ConfigureAwait(false))
                    {
                        progressCount++;
                    }

                    continue;
                }

                var envelope = new TransportEnvelope(
                    typeRegistry.GetName(eventType),
                    integrationEvent.EventId,
                    integrationEvent.CorrelationId,
                    integrationEvent.OccurredOn,
                    Encoding.UTF8.GetBytes(message.Payload))
                {
                    // The dispatch activity's context (not the stored one): consumer latency
                    // attributes to this publish, while the originating request stays
                    // reachable through the ActivityLink on the dispatch span.
                    Headers = TraceContextPropagation.Inject(activity),
                };

                await transport.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
                processedIds.Add(message.Id);
                progressCount++;
                activity?.SetTag("modulus.outcome", "published");
                metrics.OutboxMessage("published");
            }
            catch (Exception ex)
            {
                var outcome = nextAttempt >= maxAttempts ? "dead_lettered" : "retry_pending";
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("modulus.outcome", outcome);
                activity?.SetTag("modulus.attempt", nextAttempt);
                metrics.OutboxMessage(outcome);

                if (nextAttempt >= maxAttempts)
                {
                    logger.LogCritical(
                        ex,
                        "Outbox message {MessageId} failed after {Attempts} attempts and is being dead-lettered",
                        message.Id,
                        nextAttempt);
                }
                else
                {
                    logger.LogError(
                        ex,
                        "Failed to publish outbox message {MessageId} (attempt {Attempt} of {Max})",
                        message.Id,
                        nextAttempt,
                        maxAttempts);
                }

                if (await TryMarkAsFailedAsync(outboxStore, message.Id, ex.Message, nextAttemptOnUtc, cancellationToken)
                        .ConfigureAwait(false))
                {
                    progressCount++;
                }
            }
        }

        // Not wrapped in try/catch like TryMarkAsFailedAsync below: if this throws (e.g. the
        // store goes unreachable mid-flush), the rows already published this pass stay claimed
        // by _ownerId with their lease still running. That is the correct failure mode under the
        // claim model — they are not immediately re-fetched and re-published by this same
        // instance's very next pass (they would be, pre-4.0, with no claim to hold them back);
        // they simply wait out the remainder of the lease, after which any dispatcher —
        // including this one — may claim and (safely, at-least-once) re-publish them.
        if (processedIds.Count > 0)
            await outboxStore.MarkAsProcessed(_ownerId, processedIds, cancellationToken).ConfigureAwait(false);

        return progressCount;
    }

    /// <summary>
    /// Starts the per-row producer activity. When the row captured a trace context at save
    /// time, it is attached as an <see cref="ActivityLink"/> rather than a parent — the
    /// originating request finished long ago, and a link is the OTel shape for deferred/batch
    /// producers. Rows without a stored context (pre-migration rows, no sampler at save time)
    /// behave exactly as before.
    /// </summary>
    private static Activity? StartDispatchActivity(OutboxMessage message)
    {
        if (message.TraceParent is not null
            && ActivityContext.TryParse(message.TraceParent, message.TraceState, isRemote: true, out var saved))
        {
            return Source.StartActivity(
                "outbox.dispatch",
                ActivityKind.Producer,
                parentContext: default,
                links: [new ActivityLink(saved)]);
        }

        return Source.StartActivity("outbox.dispatch", ActivityKind.Producer);
    }

    // A store hiccup here must never propagate: an unhandled exception would unwind the
    // foreach loop and skip the MarkAsProcessed flush above for rows already published this
    // pass, abandoning the rest of the batch mid-loop. Logging and continuing means, at worst,
    // this one row's failure isn't durably recorded — it stays claimed by _ownerId (not
    // reverted to unclaimed) and simply waits out the remainder of this pass's lease before
    // anyone, including this instance, can claim and retry it. No hot loop, no lost row.
    private async Task<bool> TryMarkAsFailedAsync(
        IOutboxStore outboxStore,
        Guid messageId,
        string error,
        DateTime nextAttemptOnUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await outboxStore.MarkAsFailed(_ownerId, messageId, error, nextAttemptOnUtc, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to record the failed attempt for outbox message {MessageId}. It remains pending and will be retried.",
                messageId);
            return false;
        }
    }
}
