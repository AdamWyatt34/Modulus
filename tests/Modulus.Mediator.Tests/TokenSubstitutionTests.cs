using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>
/// Pins the 4.0 <see cref="RequestHandlerDelegate{TResponse}"/> cancellation-token semantics:
/// a parameterless <c>next()</c> flows the token the calling behavior itself received (never
/// silently resetting to <see langword="default"/>), while an explicit <c>next(someToken)</c>
/// substitutes that token for every inner behavior and the handler.
/// </summary>
public class TokenSubstitutionTests
{
    [Fact]
    public async Task Behavior_calling_next_with_no_args_flows_the_token_it_received_not_default()
    {
        CancellationToken observedByHandler = default;
        using var cts = new CancellationTokenSource();

        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>>(_ =>
            new TokenCapturingHandlerHelper(ct => observedByHandler = ct));
        services.AddScoped<IMediator, Mediator>();
        // A behavior that never touches cancellation must still flow whatever token it was given.
        services.AddSingleton<IPipelineBehavior<TestCommand, Result>>(
            new RecordingPassthroughBehavior<TestCommand, Result>(_ => { }));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"), cts.Token);

        observedByHandler.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Outer_behavior_substituting_a_token_flows_it_to_inner_behavior_and_handler()
    {
        using var outerCts = new CancellationTokenSource();
        using var substituteCts = new CancellationTokenSource();
        CancellationToken observedByInnerBehavior = default;
        CancellationToken observedByHandler = default;

        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>>(_ =>
            new TokenCapturingHandlerHelper(ct => observedByHandler = ct));
        services.AddScoped<IMediator, Mediator>();
        // Registration order matters: first registered = outermost (see PipelineBehaviorTests).
        services.AddSingleton<IPipelineBehavior<TestCommand, Result>>(
            new SubstitutingBehavior<TestCommand, Result>(substituteCts.Token));
        services.AddSingleton<IPipelineBehavior<TestCommand, Result>>(
            new RecordingPassthroughBehavior<TestCommand, Result>(ct => observedByInnerBehavior = ct));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"), outerCts.Token);

        // The inner behavior — closer to the handler — must observe the *substituted* token, not
        // the caller's original outerCts.Token.
        observedByInnerBehavior.ShouldBe(substituteCts.Token);

        // The inner behavior calls next() with no argument: that must flow the substituted token
        // it received (not erase it back to outerCts.Token or CancellationToken.None) all the way
        // to the handler.
        observedByHandler.ShouldBe(substituteCts.Token);
    }

    [Fact]
    public async Task TimeoutBehavior_substitutes_linked_token_so_handler_observes_its_cancellation()
    {
        var handler = new LongRunningTokenCapturingHandler();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>>(_ => handler);
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IPipelineBehavior<TestCommand, Result>>(
            new TimeoutBehavior<TestCommand, Result>(TimeSpan.FromMilliseconds(30)));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // No external cancellation token is supplied — the caller's token is never cancelled.
        // Before 4.0 this scenario was impossible: `next()` took no parameters, so a behavior
        // could never substitute a different (e.g. timeout-linked) token for the handler.
        await Should.ThrowAsync<OperationCanceledException>(
            () => mediator.Send(new TestCommand("test")));

        handler.ObservedToken.IsCancellationRequested.ShouldBeTrue();
        handler.ObservedToken.ShouldNotBe(CancellationToken.None);
    }

    private sealed class TokenCapturingHandlerHelper(Action<CancellationToken> capture) : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
        {
            capture(cancellationToken);
            return Task.FromResult(Result.Success());
        }
    }
}
