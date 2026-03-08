using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Internals;

internal sealed class IdempotentConsumerAdapter<TEvent>(IServiceProvider serviceProvider) : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var inboxStore = serviceProvider.GetService<IInboxStore>();
        var handler = serviceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();

        if (inboxStore is null)
        {
            // If no inbox is configured, fall through to direct handler execution
            await handler.Handle(context.Message, context.CancellationToken).ConfigureAwait(false);
            return;
        }

        var handlerName = handler.GetType().Name;

        // Save to inbox (idempotent - ignores duplicates)
        await inboxStore.Save(context.Message, context.CancellationToken).ConfigureAwait(false);

        // Check if this handler already processed this event
        var alreadyProcessed = await inboxStore.HasBeenProcessed(
            context.Message.EventId, handlerName, context.CancellationToken).ConfigureAwait(false);

        if (alreadyProcessed)
            return;

        await handler.Handle(context.Message, context.CancellationToken).ConfigureAwait(false);

        await inboxStore.RecordConsumer(
            context.Message.EventId, handlerName, context.CancellationToken).ConfigureAwait(false);
    }
}
