using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Dispatch;

/// <summary>
/// Resolves every registered <see cref="IIntegrationEventHandler{TEvent}"/> for an event type
/// from a scoped provider. The generic plumbing (one <c>MakeGenericMethod</c> per event type)
/// is cached, so steady-state dispatch does no reflection.
/// </summary>
internal static class HandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IReadOnlyList<HandlerDescriptor>>> Cache = new();

    public static IReadOnlyList<HandlerDescriptor> GetHandlers(IServiceProvider provider, Type eventType)
        => Cache.GetOrAdd(eventType, static type =>
            typeof(HandlerInvoker)
                .GetMethod(nameof(GetHandlersCore), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type)
                .CreateDelegate<Func<IServiceProvider, IReadOnlyList<HandlerDescriptor>>>())(provider);

    private static IReadOnlyList<HandlerDescriptor> GetHandlersCore<TEvent>(IServiceProvider provider)
        where TEvent : class, IIntegrationEvent
        => provider.GetServices<IIntegrationEventHandler<TEvent>>()
            .Select(handler => new HandlerDescriptor(
                handler.GetType().Name,
                (@event, cancellationToken) => handler.Handle((TEvent)@event, cancellationToken)))
            .ToList();
}
