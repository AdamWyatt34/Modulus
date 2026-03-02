using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public class RecordingBehavior1<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly List<string> _log;

    public RecordingBehavior1(List<string> log) => _log = log;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _log.Add("Behavior1-Before");
        var result = await next();
        _log.Add("Behavior1-After");
        return result;
    }
}

public class RecordingBehavior2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly List<string> _log;

    public RecordingBehavior2(List<string> log) => _log = log;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _log.Add("Behavior2-Before");
        var result = await next();
        _log.Add("Behavior2-After");
        return result;
    }
}
