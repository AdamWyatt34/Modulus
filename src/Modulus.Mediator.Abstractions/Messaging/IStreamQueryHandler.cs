namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Handles a streaming query that returns an asynchronous enumerable of <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to handle.</typeparam>
/// <typeparam name="TResult">The type of items in the stream.</typeparam>
public interface IStreamQueryHandler<in TQuery, out TResult>
    where TQuery : IStreamQuery<TResult>
{
    /// <summary>Handles the streaming query.</summary>
    /// <param name="query">The streaming query to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>An asynchronous stream of results.</returns>
    IAsyncEnumerable<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}
