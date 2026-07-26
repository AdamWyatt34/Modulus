namespace Modulus.Mediator;

/// <summary>
/// Controls how <see cref="Modulus.Mediator.Abstractions.IMediator.Publish{TEvent}"/> dispatches
/// a domain event to its registered
/// <see cref="Modulus.Mediator.Abstractions.IDomainEventHandler{TEvent}"/> instances.
/// </summary>
public enum PublishStrategy
{
    /// <summary>
    /// Invoke handlers one at a time, in registration order. If one or more handlers throw, every
    /// handler still runs before the collected failures are thrown together as a single
    /// <see cref="AggregateException"/>. This is the default, and matches the mediator's behavior
    /// prior to 4.0.
    /// </summary>
    Sequential,

    /// <summary>
    /// Start every handler concurrently (<see cref="Task.WhenAll(System.Threading.Tasks.Task[])"/>)
    /// and await them together. All handler failures are collected and thrown together as a single
    /// <see cref="AggregateException"/>. Because every handler is already running before
    /// cancellation can be observed, a cancelled token surfaces only after every in-flight handler
    /// has finished — handlers that had already started are never interrupted mid-flight.
    /// </summary>
    Parallel,

    /// <summary>
    /// Invoke handlers one at a time, in registration order, and rethrow immediately — unwrapped,
    /// not folded into an <see cref="AggregateException"/> — on the first handler failure. Handlers
    /// registered after the failing one never run.
    /// </summary>
    StopOnFirstFailure,
}
