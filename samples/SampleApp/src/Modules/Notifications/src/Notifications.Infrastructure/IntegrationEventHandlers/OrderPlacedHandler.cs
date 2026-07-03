using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using SampleApp.Orders.Integration.IntegrationEvents;

namespace SampleApp.Notifications.Infrastructure.IntegrationEventHandlers;

/// <summary>
/// Consumes the <see cref="OrderPlaced"/> integration event published by the Orders module
/// through the transactional outbox. A real handler would send an email or push notification;
/// the sample just logs so the cross-module flow is observable.
/// </summary>
public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
    : IIntegrationEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced @event, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Notifications module received OrderPlaced: OrderId={OrderId}, Total={Total}",
            @event.OrderId,
            @event.Total);

        return Task.CompletedTask;
    }
}
