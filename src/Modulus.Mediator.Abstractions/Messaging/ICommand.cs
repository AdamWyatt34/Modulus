namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a command that returns a <see cref="Result"/> with no value.
/// </summary>
public interface ICommand : IResultRequest
{
}

/// <summary>
/// Represents a command that returns a <see cref="Result{TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The type of the result value.</typeparam>
public interface ICommand<TResult> : IResultRequest<TResult>
{
}
