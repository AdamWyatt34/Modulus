using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

/// <summary>
/// Azure Service Bus transport built directly on Azure.Messaging.ServiceBus. Topology: a topic
/// per event type with a subscription per endpoint (Standard/Premium tier required — Basic has
/// no topics). One <see cref="ServiceBusProcessor"/> runs per subscribed topic with auto-complete
/// off; the dispatch result maps to Complete or DeadLetter. Lock auto-renewal is capped at
/// 5 minutes, which must exceed the worst-case sum of <c>ConsumerRetry</c> delays.
/// </summary>
internal sealed class AzureServiceBusTransport(
    MessagingOptions options,
    ILogger<AzureServiceBusTransport> logger) : IMessageTransport, ITransportHealthProbe
{
    private static readonly TimeSpan MaxLockRenewal = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _provisionedTopics = new(StringComparer.Ordinal);
    private readonly List<ServiceBusProcessor> _processors = [];
    private readonly SemaphoreSlim _provisionLock = new(1, 1);

    private ServiceBusClient? _client;
    private ServiceBusAdministrationClient? _adminClient;

    private ServiceBusClient Client => _client ??= CreateClient();

    private ServiceBusClient CreateClient()
        => options.Credential is not null
            ? new ServiceBusClient(options.FullyQualifiedNamespace, options.Credential)
            : new ServiceBusClient(options.ConnectionString);

    private ServiceBusAdministrationClient AdminClient => _adminClient ??=
        options.Credential is not null
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
        await sender.SendMessageAsync(
            AzureServiceBusEnvelopeMapper.ToServiceBusMessage(envelope),
            cancellationToken).ConfigureAwait(false);
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
                    await AdminClient.CreateSubscriptionAsync(topic, subscriptionName, cancellationToken).ConfigureAwait(false);
                }
            }

            var processor = Client.CreateProcessor(topic, subscriptionName, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, options.PrefetchCount),
                PrefetchCount = Math.Max(1, options.PrefetchCount),
                MaxAutoLockRenewalDuration = MaxLockRenewal,
            });

            processor.ProcessMessageAsync += async args =>
            {
                var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(args.Message);
                var result = await onMessage(envelope, args.CancellationToken).ConfigureAwait(false);

                if (result == MessageDispatchResult.Acknowledge)
                {
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "RetriesExhausted",
                        cancellationToken: args.CancellationToken).ConfigureAwait(false);
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(
                    args.Exception,
                    "Azure Service Bus processor error on {EntityPath} during {ErrorSource}.",
                    args.EntityPath,
                    args.ErrorSource);
                return Task.CompletedTask;
            };

            _processors.Add(processor);
            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureTopicExistsAsync(string topic, CancellationToken cancellationToken)
    {
        await _provisionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await AdminClient.TopicExistsAsync(topic, cancellationToken).ConfigureAwait(false))
                await AdminClient.CreateTopicAsync(topic, cancellationToken).ConfigureAwait(false);
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

    public ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // The Azure SDK connects lazily and exposes no cheap connectivity probe without
        // Manage rights; report the client's lifecycle state honestly rather than pinging.
        try
        {
            var client = Client;
            return ValueTask.FromResult(client.IsClosed
                ? new TransportHealth(false, "Azure Service Bus client is closed.")
                : new TransportHealth(true, "Azure Service Bus client is available (connectivity is verified on first use)."));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new TransportHealth(false, $"Azure Service Bus client creation failed: {ex.Message}"));
        }
    }

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

        if (_client is not null)
            await _client.DisposeAsync().ConfigureAwait(false);

        _provisionLock.Dispose();
    }
}
