using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Dispatch;

/// <summary>
/// The transport-agnostic consumer pipeline: resolves the event type, deserializes,
/// and invokes every registered handler inside a DI scope, wrapped with inbox
/// idempotency (at most once per <c>(EventId, handlerName)</c>) and in-process
/// exponential retry per <see cref="MessagingOptions.ConsumerRetry"/>. Only after all
/// retries are exhausted does it hand the message back to the transport for dead-lettering.
/// </summary>
internal sealed class ConsumerDispatcher(
    IServiceScopeFactory scopeFactory,
    MessageTypeRegistry typeRegistry,
    ILogger<ConsumerDispatcher> logger,
    MessagingOptions options)
{
    public async Task<MessageDispatchResult> DispatchAsync(
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var eventType = typeRegistry.Resolve(envelope.MessageType);
        if (eventType is null)
        {
            logger.LogWarning(
                "Received message {MessageId} with unknown or disallowed type {MessageType}. Acknowledging without dispatch.",
                envelope.MessageId,
                envelope.MessageType);
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
            return MessageDispatchResult.DeadLetter;
        }

        if (@event is not IIntegrationEvent integrationEvent)
        {
            logger.LogError(
                "Message {MessageId} of type {MessageType} deserialized to null or a non-event. Dead-lettering without retry.",
                envelope.MessageId,
                envelope.MessageType);
            return MessageDispatchResult.DeadLetter;
        }

        var maxAttempts = Math.Max(1, options.ConsumerRetry.MaxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await HandleOnce(eventType, integrationEvent, cancellationToken).ConfigureAwait(false);
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
                    return MessageDispatchResult.DeadLetter;
                }

                logger.LogWarning(
                    ex,
                    "Message {MessageId} of type {MessageType} failed (attempt {Attempt} of {Max}). Retrying.",
                    envelope.MessageId,
                    envelope.MessageType,
                    attempt,
                    maxAttempts);

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
                await handler.Handle(@event, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Save is idempotent; re-delivery re-runs only the handlers that have not recorded success.
        await inboxStore.Save(@event, cancellationToken).ConfigureAwait(false);

        foreach (var handler in handlers)
        {
            var alreadyProcessed = await inboxStore
                .HasBeenProcessed(@event.EventId, handler.Name, cancellationToken)
                .ConfigureAwait(false);

            if (alreadyProcessed)
                continue;

            await handler.Handle(@event, cancellationToken).ConfigureAwait(false);
            await inboxStore.RecordConsumer(@event.EventId, handler.Name, cancellationToken).ConfigureAwait(false);
        }
    }
}
