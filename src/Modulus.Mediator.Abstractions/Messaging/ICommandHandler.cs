namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Handles a command that returns a <see cref="Result"/> with no value.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a command that returns a <see cref="Result{TResult}"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResult">The type of the result value.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A result containing the value on success, or errors on failure.</returns>
    Task<Result<TResult>> Handle(TCommand command, CancellationToken cancellationToken = default);
}
