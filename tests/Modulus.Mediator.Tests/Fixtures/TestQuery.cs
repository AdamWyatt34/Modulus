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
