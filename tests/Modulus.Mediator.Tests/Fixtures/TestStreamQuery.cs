using System.Runtime.CompilerServices;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public record GetNumbersQuery(int Count) : IStreamQuery<int>;

public class GetNumbersQueryHandler : IStreamQueryHandler<GetNumbersQuery, int>
{
    public async IAsyncEnumerable<int> Handle(
        GetNumbersQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < query.Count; i++)
        {
            yield return i;
            await Task.Yield();
        }
    }
}
