namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a unit of work that batches changes and commits them atomically.
/// </summary>
/// <remarks>
/// When registered in DI, <see cref="Behaviors.UnitOfWorkBehavior{TRequest, TResponse}"/>
/// (in the Modulus.Mediator package) invokes <see cref="SaveChangesAsync(CancellationToken)"/>
/// after a successful command handler completes. Queries do not trigger a save.
/// If no <c>IUnitOfWork</c> is registered, the behavior is a no-op.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes within the current unit of work.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of state entries written to the underlying store.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
