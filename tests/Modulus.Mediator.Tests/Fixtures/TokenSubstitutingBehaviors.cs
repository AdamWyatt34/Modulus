using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

/// <summary>
/// Substitutes a fixed token for everything downstream, regardless of the token it was itself
/// invoked with — the shape a timeout behavior needs (there, the substituted token would be a
/// linked <see cref="System.Threading.CancellationTokenSource"/> instead of a fixed one).
/// </summary>
public sealed class SubstitutingBehavior<TRequest, TResponse>(CancellationToken tokenToSubstitute)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
        => next(tokenToSubstitute);
}

/// <summary>
/// Records whatever token it was invoked with, then calls <c>next()</c> with no argument — this
/// must flow the recorded token onward, not silently erase it back to <see langword="default"/>.
/// </summary>
public sealed class RecordingPassthroughBehavior<TRequest, TResponse>(Action<CancellationToken> onReceived)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        onReceived(cancellationToken);
        return next();
    }
}

/// <summary>
/// A timeout behavior in the MediatR-12 shape: links the caller's token with a fresh
/// timeout-bound token and substitutes the linked token for everything downstream. Before 4.0
/// this was impossible — <c>next()</c> took no parameters, so a behavior could never hand the
/// handler anything but the caller's own token.
/// </summary>
public sealed class TimeoutBehavior<TRequest, TResponse>(TimeSpan timeout) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        return await next(linkedCts.Token);
    }
}
