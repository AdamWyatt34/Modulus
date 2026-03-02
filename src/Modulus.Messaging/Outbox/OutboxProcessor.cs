using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBus _bus;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly MessagingOptions _options;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IBus bus,
        ILogger<OutboxProcessor> logger,
        MessagingOptions options)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(_options.OutboxPollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessages(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var pending = await outboxStore.GetPending(_options.OutboxBatchSize, cancellationToken);

        if (pending.Count == 0)
            return;

        var processedIds = new List<Guid>();

        foreach (var message in pending)
        {
            try
            {
                var eventType = Type.GetType(message.EventType);
                if (eventType is null)
                {
                    _logger.LogWarning(
                        "Could not resolve type {EventType} for outbox message {MessageId}",
                        message.EventType,
                        message.Id);
                    continue;
                }

                var @event = JsonSerializer.Deserialize(message.Payload, eventType);
                if (@event is null)
                {
                    _logger.LogWarning(
                        "Failed to deserialize outbox message {MessageId}",
                        message.Id);
                    continue;
                }

                await _bus.Publish(@event, eventType, cancellationToken);
                processedIds.Add(message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to publish outbox message {MessageId}",
                    message.Id);
            }
        }

        if (processedIds.Count > 0)
            await outboxStore.MarkAsProcessed(processedIds, cancellationToken);
    }
}
