using Modulus.Mediator.Abstractions;

namespace SampleApp.Notifications.Application.Samples;

public sealed class GetSampleQueryHandler : IQueryHandler<GetSampleQuery, string>
{
    public Task<Result<string>> Handle(GetSampleQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<string>.Success("Notifications module is running"));
    }
}
