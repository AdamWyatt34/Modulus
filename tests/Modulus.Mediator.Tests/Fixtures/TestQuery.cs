using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public record GetItemQuery(int Id) : IQuery<string>;

public class GetItemQueryHandler : IQueryHandler<GetItemQuery, string>
{
    public Task<Result<string>> Handle(GetItemQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<string>>($"Item-{query.Id}");
    }
}

// Implements two closed IQuery<> interfaces with different TResult so the mediator must key its
// MakeGenericMethod cache on (runtime type, TResult) rather than runtime type alone.
public record MultiResultQuery(int Id) : IQuery<int>, IQuery<string>;

public class MultiResultQueryIntHandler : IQueryHandler<MultiResultQuery, int>
{
    public Task<Result<int>> Handle(MultiResultQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<int>>(query.Id * 2);
    }
}

public class MultiResultQueryStringHandler : IQueryHandler<MultiResultQuery, string>
{
    public Task<Result<string>> Handle(MultiResultQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<string>>($"Multi-{query.Id}");
    }
}
