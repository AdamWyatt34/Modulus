using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public class PipelineBehaviorTests
{
    [Fact]
    public async Task Behaviors_execute_in_registration_order()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton(executionLog);
        services.AddPipelineBehavior(typeof(RecordingBehavior1<,>));
        services.AddPipelineBehavior(typeof(RecordingBehavior2<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        // Behavior1 registered first -> outermost -> executes before/after Behavior2
        executionLog.ShouldBe([
            "Behavior1-Before",
            "Behavior2-Before",
            "Behavior2-After",
            "Behavior1-After"
        ]);
    }

    [Fact]
    public async Task Short_circuit_behavior_returns_failure_without_calling_handler()
    {
        var handlerCalled = false;
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>>(
            _ => new TrackingCommandHandler(() => handlerCalled = true));
        services.AddScoped<IMediator, Mediator>();
        services.AddPipelineBehavior(typeof(ShortCircuitBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ShortCircuit");
        handlerCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Early_behavior_short_circuits_later_behavior_never_runs()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton(executionLog);
        // ShortCircuit registered first (outermost) -> short-circuits before Behavior1 runs
        services.AddPipelineBehavior(typeof(ShortCircuitBehavior<,>));
        services.AddPipelineBehavior(typeof(RecordingBehavior1<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsFailure.ShouldBeTrue();
        executionLog.ShouldBeEmpty(); // RecordingBehavior1 never ran
    }

    [Fact]
    public async Task Zero_behaviors_calls_handler_directly()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        // No behaviors registered at all
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Behaviors_apply_to_queries()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<GetItemQuery, string>, GetItemQueryHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton(executionLog);
        services.AddPipelineBehavior(typeof(RecordingBehavior1<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Query(new GetItemQuery(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Item-1");
        executionLog.ShouldBe(["Behavior1-Before", "Behavior1-After"]);
    }

    private class TrackingCommandHandler : ICommandHandler<TestCommand>
    {
        private readonly Action _onHandle;

        public TrackingCommandHandler(Action onHandle) => _onHandle = onHandle;

        public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
        {
            _onHandle();
            return Task.FromResult(Result.Success());
        }
    }
}
