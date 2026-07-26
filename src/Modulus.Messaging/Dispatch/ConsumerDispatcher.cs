using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Dispatch;

/// <summary>
/// The transport-agnostic consumer pipeline: resolves the event type, deserializes,
/// and invokes every registered handler inside a DI scope, wrapped with inbox
/// idempotency and in-process exponential retry per <see cref="MessagingOptions.ConsumerRetry"/>.
/// Idempotency is reservation-based: each <c>(EventId, handlerName)</c> pair is claimed in the
/// inbox before the handler runs, so concurrent duplicate deliveries execute a handler exactly
/// once, and a crashed owner's reservation goes stale
/// (<see cref="MessagingOptions.ConsumerReservationTimeout"/>) and is taken over by a
/// redelivery or dead-letter replay — preserving at-least-once delivery. Only after all
/// retries are exhausted does it hand the message back to the transport for dead-lettering.
/// </summary>
internal sealed class ConsumerDispatcher(
    IServiceScopeFactory scopeFactory,
    MessageTypeRegistry typeRegistry,
    ILogger<ConsumerDispatcher> logger,
    MessagingOptions options,
    MessagingMetrics metrics)
{
    private static readonly ActivitySource Source = new(MessagingDiagnostics.ActivitySourceName);

    public async Task<MessageDispatchResult> DispatchAsync(
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // One Consumer span per delivery, parented on the producer context that rode the
        // envelope headers across the broker — this is what makes end-to-end traces possible.
        // It spans the whole in-process retry loop; Activity.Current flows into handlers (and
        // any TracingBehavior-instrumented mediator calls they make) for free.
        using var activity = StartConsumeActivity(envelope);

        var result = await DispatchCoreAsync(envelope, activity, cancellationToken).ConfigureAwait(false);

        activity?.SetTag("modulus.outcome", result == MessageDispatchResult.Acknowledge ? "acknowledge" : "dead_letter");
        if (result == MessageDispatchResult.DeadLetter)
            activity?.SetStatus(ActivityStatusCode.Error, "Message was dead-lettered.");

        return result;
    }

    private static Activity? StartConsumeActivity(TransportEnvelope envelope)
    {
        var activity = TraceContextPropagation.TryExtract(envelope.Headers, out var parentContext)
            ? Source.StartActivity($"{envelope.MessageType} process", ActivityKind.Consumer, parentContext)
            : Source.StartActivity($"{envelope.MessageType} process", ActivityKind.Consumer);

        activity?.SetTag("modulus.message_id", envelope.MessageId);
        activity?.SetTag("modulus.message_type", envelope.MessageType);
        return activity;
    }

    private async Task<MessageDispatchResult> DispatchCoreAsync(
        TransportEnvelope envelope,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var eventType = typeRegistry.Resolve(envelope.MessageType);
        if (eventType is null)
        {
            logger.LogWarning(
                "Received message {MessageId} with unknown or disallowed type {MessageType}. Acknowledging without dispatch.",
                envelope.MessageId,
                envelope.MessageType);
            activity?.SetTag("modulus.dispatch", "unknown_type");
            return MessageDispatchResult.Acknowledge;
        }

        object? @event;
        try
        {
            @event = MessageSerializer.Deserialize(envelope.Body, eventType);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Message {MessageId} of type {MessageType} has an unreadable body. Dead-lettering without retry.",
                envelope.MessageId,
                envelope.MessageType);
            metrics.ConsumerDeadLettered(envelope.MessageType);
            activity?.SetTag("modulus.dispatch", "deserialize_failed");
            return MessageDispatchResult.DeadLetter;
        }

        if (@event is not IIntegrationEvent integrationEvent)
        {
            logger.LogError(
                "Message {MessageId} of type {MessageType} deserialized to null or a non-event. Dead-lettering without retry.",
                envelope.MessageId,
                envelope.MessageType);
            metrics.ConsumerDeadLettered(envelope.MessageType);
            return MessageDispatchResult.DeadLetter;
        }

        var maxAttempts = Math.Max(1, options.ConsumerRetry.MaxAttempts);

        // Handlers whose reservation this dispatch already holds: a retry attempt must
        // re-execute them rather than see its own reservation as foreign and back off.
        var reservedByThisDispatch = new HashSet<string>(StringComparer.Ordinal);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await HandleOnce(eventType, integrationEvent, reservedByThisDispatch, cancellationToken).ConfigureAwait(false);
                return MessageDispatchResult.Acknowledge;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(
                        ex,
                        "Message {MessageId} of type {MessageType} failed after {Attempts} attempts and is being dead-lettered.",
                        envelope.MessageId,
                        envelope.MessageType,
                        attempt);
                    metrics.ConsumerDeadLettered(envelope.MessageType);

                    // Release this dispatch's own reservations so a prompt DLQ replay can
                    // reserve and execute immediately instead of failing with
                    // InboxReservationPendingException against a still-fresh reservation for
                    // up to ConsumerReservationTimeout.
                    await ReleaseReservationsAsync(integrationEvent.EventId, reservedByThisDispatch, cancellationToken)
                        .ConfigureAwait(false);

                    return MessageDispatchResult.DeadLetter;
                }

                logger.LogWarning(
                    ex,
                    "Message {MessageId} of type {MessageType} failed (attempt {Attempt} of {Max}). Retrying.",
                    envelope.MessageId,
                    envelope.MessageType,
                    attempt,
                    maxAttempts);
                metrics.ConsumerRetry(envelope.MessageType);
                activity?.SetTag("modulus.attempt", attempt + 1);

                var delay = RetryDelayCalculator.GetDelay(options.ConsumerRetry, attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        return MessageDispatchResult.DeadLetter;
    }

    private async Task HandleOnce(
        Type eventType,
        IIntegrationEvent @event,
        HashSet<string> reservedByThisDispatch,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var handlers = HandlerInvoker.GetHandlers(scope.ServiceProvider, eventType);
        if (handlers.Count == 0)
            return;

        var inboxStore = scope.ServiceProvider.GetService<IInboxStore>();

        if (inboxStore is null)
        {
            // No inbox configured: direct execution with no deduplication.
            foreach (var handler in handlers)
                await HandleTimed(handler, @event, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Save is idempotent; re-delivery re-runs only the handlers that have not completed.
        await inboxStore.Save(@event, cancellationToken).ConfigureAwait(false);

        foreach (var handler in handlers)
        {
            if (!reservedByThisDispatch.Contains(handler.Name))
            {
                var alreadyProcessed = await inboxStore
                    .HasBeenProcessed(@event.EventId, handler.Name, cancellationToken)
                    .ConfigureAwait(false);

                if (alreadyProcessed)
                {
                    metrics.InboxDeduplicated(handler.Name);
                    continue;
                }

                var reserved = await inboxStore
                    .TryReserve(@event.EventId, handler.Name, options.ConsumerReservationTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (!reserved)
                {
                    // The pair may have completed between the check and the reserve.
                    if (await inboxStore.HasBeenProcessed(@event.EventId, handler.Name, cancellationToken).ConfigureAwait(false))
                    {
                        metrics.InboxDeduplicated(handler.Name);
                        continue;
                    }

                    throw new InboxReservationPendingException(@event.EventId, handler.Name);
                }

                reservedByThisDispatch.Add(handler.Name);
            }

            await HandleTimed(handler, @event, cancellationToken).ConfigureAwait(false);
            await inboxStore.MarkConsumerProcessed(@event.EventId, handler.Name, cancellationToken).ConfigureAwait(false);

            // Completed pairs are covered by the HasBeenProcessed fast path from here on;
            // dropping them keeps a later retry from re-executing a succeeded handler.
            reservedByThisDispatch.Remove(handler.Name);
        }
    }

    private async Task HandleTimed(
        HandlerDescriptor handler,
        IIntegrationEvent @event,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await handler.Handle(@event, cancellationToken).ConfigureAwait(false);
            metrics.HandlerDuration(Stopwatch.GetElapsedTime(start).TotalMilliseconds, handler.Name, "success");
        }
        catch
        {
            metrics.HandlerDuration(Stopwatch.GetElapsedTime(start).TotalMilliseconds, handler.Name, "failure");
            throw;
        }
    }

    // Best-effort: releasing is a courtesy to a future replay, not a correctness requirement
    // (the reservation would otherwise simply go stale after ConsumerReservationTimeout, same
    // as before this dispatch existed). A release failure must not mask the dead-letter
    // outcome already decided by the caller, so each handler's release is isolated.
    private async Task ReleaseReservationsAsync(
        Guid eventId,
        IReadOnlyCollection<string> handlerNames,
        CancellationToken cancellationToken)
    {
        if (handlerNames.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var inboxStore = scope.ServiceProvider.GetService<IInboxStore>();
        if (inboxStore is null)
            return;

        foreach (var handlerName in handlerNames)
        {
            try
            {
                await inboxStore.ReleaseReservation(eventId, handlerName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to release the inbox reservation for handler {HandlerName} on message {EventId} after dead-lettering. " +
                    "It will remain reserved until the timeout elapses.",
                    handlerName,
                    eventId);
            }
        }
    }
}
