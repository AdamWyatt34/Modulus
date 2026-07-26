using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

/// <summary>
/// Azure Service Bus transport built directly on Azure.Messaging.ServiceBus. Topology: a topic
/// per event type with a subscription per endpoint (Standard/Premium tier required — Basic has
/// no topics). One <see cref="ServiceBusProcessor"/> runs per subscribed topic with auto-complete
/// off; the dispatch result maps to Complete or DeadLetter. Lock auto-renewal is computed from
/// <see cref="MessagingOptions.ConsumerRetry"/>'s worst-case delay budget plus a safety margin
/// (see <see cref="ComputeMaxAutoLockRenewalDuration"/>) instead of a fixed window, so it always
/// exceeds the in-process retry loop it needs to outlive.
/// </summary>
internal sealed class AzureServiceBusTransport(
    MessagingOptions options,
    ILogger<AzureServiceBusTransport> logger) : IMessageTransport, ITransportHealthProbe
{
    /// <summary>
    /// Fixed buffer added on top of the computed <see cref="MessagingOptions.ConsumerRetry"/>
    /// delay budget, covering actual handler execution time, dispatch overhead, and clock skew
    /// that the retry sleeps alone don't account for.
    /// </summary>
    private static readonly TimeSpan LockRenewalSafetyMargin = TimeSpan.FromMinutes(2);

    /// <summary>Floor for the computed lock renewal duration, even with ConsumerRetry.MaxAttempts == 1.</summary>
    private static readonly TimeSpan MinimumLockRenewal = TimeSpan.FromMinutes(1);

    /// <summary>Short timeout for the namespace connectivity probe in <see cref="CheckHealthAsync"/>.</summary>
    private static readonly TimeSpan NamespaceProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _provisionedTopics = new(StringComparer.Ordinal);

    // Keyed by EntityPath: a missing topic/subscription observed via ProcessErrorAsync, surfaced
    // by CheckHealthAsync so AutoProvision=false against an entity that doesn't exist yet is
    // visibly unhealthy instead of looping silently. Cleared per-entity as soon as a message is
    // actually delivered for it (proof the entity currently exists).
    private readonly ConcurrentDictionary<string, MissingEntityFault> _missingEntityFaults = new(StringComparer.Ordinal);

    private readonly List<ServiceBusProcessor> _processors = [];
    private readonly SemaphoreSlim _provisionLock = new(1, 1);
    private readonly Lock _clientLock = new();
    private readonly Lock _adminClientLock = new();

    private ServiceBusClient? _client;
    private ServiceBusAdministrationClient? _adminClient;

    private ServiceBusClient Client
    {
        get
        {
            if (_client is { } existing)
                return existing;

            lock (_clientLock)
            {
                return _client ??= CreateClient();
            }
        }
    }

    private ServiceBusAdministrationClient AdminClient
    {
        get
        {
            if (_adminClient is { } existing)
                return existing;

            lock (_adminClientLock)
            {
                return _adminClient ??= CreateAdminClient();
            }
        }
    }

    private ServiceBusClient CreateClient()
        => options.Credential is not null
            ? new ServiceBusClient(options.FullyQualifiedNamespace, options.Credential)
            : new ServiceBusClient(options.ConnectionString);

    private ServiceBusAdministrationClient CreateAdminClient()
        => options.Credential is not null
            ? new ServiceBusAdministrationClient(options.FullyQualifiedNamespace, options.Credential)
            : new ServiceBusAdministrationClient(options.ConnectionString);

    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var topic = AzureServiceBusTopology.TopicName(envelope.MessageType);

        if (options.AutoProvision && !_provisionedTopics.ContainsKey(topic))
        {
            await EnsureTopicExistsAsync(topic, cancellationToken).ConfigureAwait(false);
            _provisionedTopics.TryAdd(topic, true);
        }

        var sender = _senders.GetOrAdd(topic, name => Client.CreateSender(name));

        try
        {
            await sender.SendMessageAsync(
                AzureServiceBusEnvelopeMapper.ToServiceBusMessage(envelope),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            // The topic existed at provisioning time but was deleted out-of-band. Evict both
            // caches so the next publish re-declares the topic (when AutoProvision is on) and
            // opens a fresh sender, instead of repeating this failure forever against state that
            // still believes the topic is provisioned and a sender link bound to a dead entity.
            _provisionedTopics.TryRemove(topic, out _);

            if (_senders.TryRemove(topic, out var staleSender))
                await staleSender.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    public async Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default)
    {
        var endpointName = EndpointNameResolver.Resolve(options);
        var subscriptionName = AzureServiceBusTopology.SubscriptionName(endpointName);

        foreach (var subscription in subscriptions)
        {
            var topic = AzureServiceBusTopology.TopicName(subscription.MessageTypeName);

            if (options.AutoProvision)
            {
                await EnsureTopicExistsAsync(topic, cancellationToken).ConfigureAwait(false);
                _provisionedTopics.TryAdd(topic, true);

                if (!await AdminClient.SubscriptionExistsAsync(topic, subscriptionName, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await AdminClient.CreateSubscriptionAsync(topic, subscriptionName, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
                    {
                        // A concurrent replica created the subscription between our exists-check
                        // and our create call; the subscription existing is the desired end
                        // state, not a failure.
                    }
                }
            }

            var processor = Client.CreateProcessor(topic, subscriptionName, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, options.PrefetchCount),
                // SDK default (0), not options.PrefetchCount: a message prefetched ahead of a
                // free MaxConcurrentCalls slot gets no automatic lock renewal until a handler
                // actually picks it up, so under load a prefetched-but-unstarted message can have
                // its lock expire before ProcessMessageAsync ever runs — a spurious duplicate
                // delivery. MaxConcurrentCalls (above) is what bounds concurrency; PrefetchCount
                // previously doubled as both.
                PrefetchCount = 0,
                MaxAutoLockRenewalDuration = ComputeMaxAutoLockRenewalDuration(options.ConsumerRetry),
            });

            var subscriptionTopic = topic;

            processor.ProcessMessageAsync += async args =>
            {
                // A message was delivered for this entity, proving it currently exists and is
                // reachable: clear any previously recorded "missing entity" fault for it.
                _missingEntityFaults.TryRemove(args.EntityPath, out _);

                var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(args.Message);
                var result = await onMessage(envelope, args.CancellationToken).ConfigureAwait(false);

                if (result == MessageDispatchResult.Retry)
                {
                    // Schedule the delayed copy before settling the original; a scheduling
                    // failure abandons the original for an immediate redelivery so no attempt
                    // is lost. Topics have no per-subscription send, so the copy fans out with
                    // a target-endpoint property and foreign endpoints acknowledge it unrun.
                    try
                    {
                        await ScheduleRedeliveryAsync(subscriptionTopic, envelope, endpointName, args.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Failed to schedule broker redelivery for message {MessageId}; abandoning for immediate redelivery instead.",
                            envelope.MessageId);
                        await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                // Settle with CancellationToken.None: the handler already ran to completion (or
                // exhausted retries) by this point, so a shutdown-triggered cancellation on
                // args.CancellationToken must not prevent telling the broker the outcome — that
                // would abandon an already-processed message and force a duplicate redelivery on
                // the next deploy.
                if (result is MessageDispatchResult.Acknowledge or MessageDispatchResult.Retry)
                {
                    await args.CompleteMessageAsync(args.Message, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "RetriesExhausted",
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(
                    args.Exception,
                    "Azure Service Bus processor error on {EntityPath} during {ErrorSource}.",
                    args.EntityPath,
                    args.ErrorSource);

                if (args.Exception is ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound } sbEx)
                {
                    // Under AutoProvision=false against a topic/subscription that doesn't exist,
                    // StartProcessingAsync succeeds and this fires repeatedly with no other
                    // visible signal; record it so CheckHealthAsync can report unhealthy.
                    _missingEntityFaults[args.EntityPath] = new MissingEntityFault(
                        DateTimeOffset.UtcNow, args.EntityPath, sbEx.Message);
                }

                return Task.CompletedTask;
            };

            _processors.Add(processor);
            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Schedules the failed message's delayed copy back onto its topic with the incremented
    /// attempt header, the endpoint that owns the retry, and
    /// <see cref="ServiceBusMessage.ScheduledEnqueueTime"/> as the backoff. Other endpoints'
    /// subscriptions receive the copy too (topics cannot target one subscription) and
    /// acknowledge it without dispatching via the target-endpoint header.
    /// </summary>
    private async Task ScheduleRedeliveryAsync(
        string topic,
        TransportEnvelope envelope,
        string endpointName,
        CancellationToken cancellationToken)
    {
        var attempt = RedeliveryHeaders.GetAttempt(envelope);
        var delay = RetryDelayCalculator.GetDelay(options.ConsumerRetry, attempt);

        var copy = envelope with
        {
            Headers = RedeliveryHeaders.ForRedelivery(envelope, targetEndpoint: endpointName),
            ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow + delay,
        };

        var sender = _senders.GetOrAdd(topic, name => Client.CreateSender(name));
        await sender.SendMessageAsync(
            AzureServiceBusEnvelopeMapper.ToServiceBusMessage(copy),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTopicExistsAsync(string topic, CancellationToken cancellationToken)
    {
        await _provisionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await AdminClient.TopicExistsAsync(topic, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                await AdminClient.CreateTopicAsync(topic, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
            {
                // A concurrent replica created the topic between our exists-check and our create
                // call; the topic existing is the desired end state, not a failure.
            }
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            throw new InvalidOperationException(
                "Failed to provision Azure Service Bus topology. Topics require the Standard or Premium tier " +
                "(the Basic tier has no topics), and AutoProvision requires Manage rights. " +
                "Pre-create the entities and set MessagingOptions.AutoProvision to false for least-privilege deployments.",
                ex);
        }
        finally
        {
            _provisionLock.Release();
        }
    }

    /// <summary>
    /// Computes the lock auto-renewal window from the worst-case total delay
    /// <see cref="MessagingOptions.ConsumerRetry"/> spends sleeping between attempts (mirroring
    /// <see cref="ConsumerDispatcher"/>'s own accounting), plus
    /// <see cref="LockRenewalSafetyMargin"/> for the handler execution time the sleeps alone
    /// don't cover. A hardcoded window can't track a user-configured retry budget: too short,
    /// and a message still mid-retry loses its lock and is redelivered as a duplicate while
    /// still being processed.
    /// </summary>
    internal static TimeSpan ComputeMaxAutoLockRenewalDuration(RetryPolicyOptions consumerRetry)
    {
        var maxAttempts = Math.Max(1, consumerRetry.MaxAttempts);
        var totalDelay = TimeSpan.Zero;

        // ConsumerDispatcher.DispatchAsync sleeps after every failed attempt except the last,
        // which dead-letters immediately with no further wait.
        for (var attempt = 1; attempt < maxAttempts; attempt++)
            totalDelay += RetryDelayCalculator.GetDelay(consumerRetry, attempt);

        var withMargin = totalDelay + LockRenewalSafetyMargin;
        return withMargin > MinimumLockRenewal ? withMargin : MinimumLockRenewal;
    }

    public async ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = Client;
            if (client.IsClosed)
                return new TransportHealth(false, "Azure Service Bus client is closed.");

            // A missing topic/subscription under AutoProvision=false lets StartProcessingAsync
            // succeed while ProcessErrorAsync loops on MessagingEntityNotFound forever with no
            // other visible signal; surface it instead of reporting healthy indefinitely.
            if (!_missingEntityFaults.IsEmpty)
            {
                var fault = _missingEntityFaults.Values.OrderByDescending(f => f.OccurredAtUtc).First();
                return new TransportHealth(
                    false,
                    $"Azure Service Bus entity '{fault.EntityPath}' was unreachable as of {fault.OccurredAtUtc:O}: " +
                    $"{fault.Reason} If AutoProvision is false, pre-create the missing topic/subscription.");
            }

            return await ProbeNamespaceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new TransportHealth(false, $"Azure Service Bus client creation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts a real namespace call when an admin client is constructible, rather than only
    /// reporting the client's local lifecycle state. <c>GetNamespacePropertiesAsync</c> works
    /// with any claim (Send/Listen/Manage), so it is usable even by least-privilege credentials.
    /// </summary>
    private async Task<TransportHealth> ProbeNamespaceAsync(CancellationToken cancellationToken)
    {
        ServiceBusAdministrationClient adminClient;
        try
        {
            adminClient = AdminClient;
        }
        catch (Exception ex)
        {
            // No usable connection string/credential for the management surface at all: fall
            // back to the best-effort lifecycle check below rather than failing health for what
            // may be a deliberate configuration choice.
            return BestEffortHealth(ex);
        }

        using var timeoutSource = new CancellationTokenSource(NamespaceProbeTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await adminClient.GetNamespacePropertiesAsync(linkedSource.Token).ConfigureAwait(false);
            return new TransportHealth(true, "Azure Service Bus namespace is reachable.");
        }
        catch (UnauthorizedAccessException ex)
        {
            // Some deployments intentionally scope credentials below what
            // GetNamespacePropertiesAsync requires; that is a valid least-privilege
            // configuration, not an outage, so fall back rather than failing health.
            return BestEffortHealth(ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TransportHealth(
                false,
                $"Azure Service Bus namespace probe did not respond within {NamespaceProbeTimeout.TotalSeconds}s.");
        }
        catch (ServiceBusException ex)
        {
            return new TransportHealth(false, $"Azure Service Bus namespace probe failed: {ex.Message}");
        }
    }

    private static TransportHealth BestEffortHealth(Exception probeFailure)
        => new(
            true,
            "Azure Service Bus client is available (connectivity is verified on first use); " +
            $"namespace probe unavailable: {probeFailure.Message}");

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        foreach (var processor in _processors)
        {
            if (processor.IsProcessing)
                await processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
            await processor.DisposeAsync().ConfigureAwait(false);

        foreach (var sender in _senders.Values)
            await sender.DisposeAsync().ConfigureAwait(false);

        // ServiceBusAdministrationClient exposes no Dispose/DisposeAsync: it holds no long-lived
        // connection, only an HTTP pipeline, so there is nothing to dispose for _adminClient.
        if (_client is not null)
            await _client.DisposeAsync().ConfigureAwait(false);

        _provisionLock.Dispose();
    }

    /// <summary>
    /// A <see cref="ServiceBusFailureReason.MessagingEntityNotFound"/> observed via
    /// <see cref="ServiceBusProcessor.ProcessErrorAsync"/>.
    /// </summary>
    private sealed record MissingEntityFault(DateTimeOffset OccurredAtUtc, string EntityPath, string Reason);
}
