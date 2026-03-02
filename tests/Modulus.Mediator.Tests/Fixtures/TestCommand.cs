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
