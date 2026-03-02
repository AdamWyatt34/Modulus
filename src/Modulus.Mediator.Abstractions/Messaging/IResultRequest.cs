namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Infrastructure marker interface for requests returning <see cref="Result"/>.
/// Do not implement this interface directly; use <see cref="ICommand"/> instead.
/// </summary>
public interface IResultRequest
{
}

/// <summary>
/// Infrastructure marker interface for requests returning <see cref="Result{TResult}"/>.
/// Do not implement this interface directly; use <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/> instead.
/// </summary>
/// <typeparam name="TResult">The type of the result value.</typeparam>
public interface IResultRequest<TResult>
{
}
