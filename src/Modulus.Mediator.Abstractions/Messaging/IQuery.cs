namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a query that returns a <see cref="Result{TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The type of the result value.</typeparam>
public interface IQuery<TResult> : IResultRequest<TResult>
{
}
