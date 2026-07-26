using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>
/// Pins the ordering/failure/aggregation/cancellation semantics of each <see cref="PublishStrategy"/>,
/// the <see cref="MediatorOptions"/> DI plumbing, and that <see cref="PublishStrategy.Sequential"/>
/// remains the default (matching the mediator's behavior prior to 4.0).
/// </summary>
public class PublishStrategyTests
{
    [Fact]
    public void MediatorOptions_default_PublishStrategy_is_Sequential()
    {
        new MediatorOptions().PublishStrategy.ShouldBe(PublishStrategy.Sequential);
    }

    [Fact]
    public void AddModulusMediator_without_configure_registers_MediatorOptions_with_Sequential_default()
    {
        var services = new ServiceCollection();

        services.AddModulusMediator();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorOptions>();
        options.PublishStrategy.ShouldBe(PublishStrategy.Sequential);
    }

    [Fact]
    public void AddModulusMediator_with_configure_registers_the_configured_strategy()
    {
        var services = new ServiceCollection();

        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorOptions>();
        options.PublishStrategy.ShouldBe(PublishStrategy.Parallel);
    }

    [Fact]
    public async Task No_MediatorOptions_registered_behaves_as_Sequential()
    {
        // A container built by hand (bypassing AddModulusMediator) has no MediatorOptions at
        // all — the pre-4.0 default (Sequential: run every handler, aggregate failures) must
        // still be what Publish does.
        var handler1 = new OrderPlacedHandler1();
        var failingHandler = new FailingOrderPlacedHandler();
        var handler2 = new OrderPlacedHandler2();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(failingHandler);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<AggregateException>(
            () => mediator.Publish(new OrderPlacedEvent(1)));

        ex.InnerExceptions.Count.ShouldBe(1);
        handler1.HandledOrderIds.ShouldBe([1]);
        handler2.HandledOrderIds.ShouldBe([1]);
    }

    [Fact]
    public async Task Sequential_strategy_runs_handlers_in_registration_order()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new OrderRecordingHandler("first", order));
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new OrderRecordingHandler("second", order));
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Sequential);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderPlacedEvent(1));

        order.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Sequential_strategy_aggregates_every_handler_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("A"));
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("B"));
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Sequential);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<AggregateException>(
            () => mediator.Publish(new OrderPlacedEvent(1)));

        ex.InnerExceptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sequential_strategy_stops_dispatch_and_throws_when_cancelled_between_handlers()
    {
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new CancelingOrderPlacedHandler(cts));
        var handler2 = new OrderPlacedHandler2();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Sequential);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => mediator.Publish(new OrderPlacedEvent(1), cts.Token));

        handler2.HandledOrderIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task StopOnFirstFailure_strategy_never_runs_handlers_after_the_failing_one()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new OrderRecordingHandler("first", order));
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("failing"));
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new OrderRecordingHandler("never-runs", order));
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.StopOnFirstFailure);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Publish(new OrderPlacedEvent(1)));

        order.ShouldBe(["first"]);
    }

    [Fact]
    public async Task StopOnFirstFailure_strategy_rethrows_the_raw_exception_unwrapped()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("A"));
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.StopOnFirstFailure);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Unlike Sequential/Parallel, a single handler failure is not folded into an
        // AggregateException — the original exception type propagates directly.
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Publish(new OrderPlacedEvent(1)));

        ex.Message.ShouldContain("A");
    }

    [Fact]
    public async Task StopOnFirstFailure_strategy_stops_dispatch_and_throws_when_cancelled_between_handlers()
    {
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new CancelingOrderPlacedHandler(cts));
        var handler2 = new OrderPlacedHandler2();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.StopOnFirstFailure);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => mediator.Publish(new OrderPlacedEvent(1), cts.Token));

        handler2.HandledOrderIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Parallel_strategy_invokes_every_handler_concurrently()
    {
        // Each handler signals it has started, then awaits the OTHER handler's signal before
        // completing. This can only succeed if both handlers run concurrently — under a
        // sequential strategy this would deadlock (and the awaited WaitAsync below would time out).
        var handler1Started = new TaskCompletionSource();
        var handler2Started = new TaskCompletionSource();
        var handler1 = new GateHandler(handler1Started, handler2Started.Task);
        var handler2 = new GateHandler(handler2Started, handler1Started.Task);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderPlacedEvent(1)).WaitAsync(TimeSpan.FromSeconds(5));

        handler1.Completed.ShouldBeTrue();
        handler2.Completed.ShouldBeTrue();
    }

    [Fact]
    public async Task Parallel_strategy_aggregates_every_handler_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("A"));
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(new AlwaysFailingHandler("B"));
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<AggregateException>(
            () => mediator.Publish(new OrderPlacedEvent(1)));

        ex.InnerExceptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Parallel_strategy_still_runs_a_handler_started_before_cancellation_was_observed()
    {
        // Unlike Sequential/StopOnFirstFailure, Parallel starts every handler before cancellation
        // can be observed, so a handler already in flight when another handler cancels the token
        // is NOT skipped — it was already running.
        using var cts = new CancellationTokenSource();
        var cancelingHandlerFinished = new TaskCompletionSource();
        var cancelingHandler = new CancelingThenSignalingHandler(cts, cancelingHandlerFinished);
        var handler2 = new WaitThenRecordHandler(cancelingHandlerFinished.Task);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(cancelingHandler);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => mediator.Publish(new OrderPlacedEvent(1), cts.Token));

        // handler2 was already started (Parallel starts everything up front) and is documented to
        // run to completion even though the token was cancelled by a sibling handler.
        handler2.Completed.ShouldBeTrue();
    }

    [Fact]
    public async Task Parallel_strategy_stops_before_dispatching_when_token_already_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new OrderPlacedHandler1();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler);
        services.AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => mediator.Publish(new OrderPlacedEvent(1), cts.Token));

        handler.HandledOrderIds.ShouldBeEmpty();
    }

    private sealed class OrderRecordingHandler(string name, List<string> order) : IDomainEventHandler<OrderPlacedEvent>
    {
        public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailingHandler(string name) : IDomainEventHandler<OrderPlacedEvent>
    {
        public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Handler {name} failed");
    }

    private sealed class GateHandler(TaskCompletionSource ownSignal, Task waitFor) : IDomainEventHandler<OrderPlacedEvent>
    {
        public bool Completed { get; private set; }

        public async Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            ownSignal.SetResult();
            await waitFor;
            Completed = true;
        }
    }

    private sealed class CancelingThenSignalingHandler(CancellationTokenSource cts, TaskCompletionSource signal)
        : IDomainEventHandler<OrderPlacedEvent>
    {
        public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            signal.SetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class WaitThenRecordHandler(Task waitFor) : IDomainEventHandler<OrderPlacedEvent>
    {
        public bool Completed { get; private set; }

        public async Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            await waitFor;
            Completed = true;
        }
    }
}
