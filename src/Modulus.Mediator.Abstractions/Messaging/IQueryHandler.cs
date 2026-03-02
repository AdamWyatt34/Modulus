namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Handles a query that returns a <see cref="Result{TResult}"/>.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle.</typeparam>
/// <typeparam name="TResult">The type of the result value.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result containing the queried value on success, or errors on failure.</returns>
    Task<Result<TResult>> Handle(TQuery query, CancellationToken cancellationToken = default);
}
