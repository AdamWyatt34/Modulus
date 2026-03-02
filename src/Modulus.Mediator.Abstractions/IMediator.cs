namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Dispatches commands, queries, streaming queries, and domain events to their handlers.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a command that returns a <see cref="Result"/> with no value.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command that returns a <see cref="Result{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">The type of the result value.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result containing the value on success, or errors on failure.</returns>
    Task<Result<TResult>> Send<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a query that returns a <see cref="Result{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">The type of the result value.</typeparam>
    /// <param name="query">The query to dispatch.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result containing the queried value on success, or errors on failure.</returns>
    Task<Result<TResult>> Query<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a streaming query that returns an asynchronous enumerable.
    /// </summary>
    /// <typeparam name="TResult">The type of items in the stream.</typeparam>
    /// <param name="query">The streaming query to dispatch.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>An asynchronous stream of results.</returns>
    IAsyncEnumerable<TResult> Stream<TResult>(
        IStreamQuery<TResult> query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a domain event to all registered handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of domain event to publish.</typeparam>
    /// <param name="domainEvent">The domain event to publish.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Publish<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
