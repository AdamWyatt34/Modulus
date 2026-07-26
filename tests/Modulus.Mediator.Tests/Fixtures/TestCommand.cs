using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public record TestCommand(string Name) : ICommand;

public class TestCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }
}

public class FailingTestCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure(Error.Failure("TestError", "Something went wrong")));
    }
}

public class ErrorImplicitConversionCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        // Demonstrates implicit conversion from Error to Result
        return Task.FromResult<Result>(Error.NotFound("NotFound", "Item not found"));
    }
}

public class ThrowingCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Handler exploded");
    }
}

/// <summary>
/// Captures the token it was invoked with, then waits on it indefinitely — used to prove a
/// pipeline behavior's substituted token (e.g. a timeout's linked token) reaches the handler and
/// actually cancels its work.
/// </summary>
public class LongRunningTokenCapturingHandler : ICommandHandler<TestCommand>
{
    public CancellationToken ObservedToken { get; private set; }

    public async Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        ObservedToken = cancellationToken;
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Result.Success();
    }
}
