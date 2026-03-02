using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public record CreateItemCommand(string Name) : ICommand<int>;

public class CreateItemCommandHandler : ICommandHandler<CreateItemCommand, int>
{
    public Task<Result<int>> Handle(CreateItemCommand command, CancellationToken cancellationToken = default)
    {
        // Demonstrates implicit conversion from TValue to Result<TValue>
        return Task.FromResult<Result<int>>(42);
    }
}

public class FailingCreateItemCommandHandler : ICommandHandler<CreateItemCommand, int>
{
    public Task<Result<int>> Handle(CreateItemCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<int>.Failure(Error.Failure("CreateFailed", "Could not create item")));
    }
}

public class ErrorImplicitConversionCreateItemHandler : ICommandHandler<CreateItemCommand, int>
{
    public Task<Result<int>> Handle(CreateItemCommand command, CancellationToken cancellationToken = default)
    {
        // Demonstrates implicit conversion from Error to Result<T>
        return Task.FromResult<Result<int>>(Error.Conflict("Conflict", "Item already exists"));
    }
}
