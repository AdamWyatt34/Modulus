using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public class MediatorTests
{
    [Fact]
    public async Task Send_Command_returning_Result_success()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsSuccess.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Send_Command_returning_Result_failure()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, FailingTestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].Code.ShouldBe("TestError");
    }

    [Fact]
    public async Task Send_Command_returning_ResultT_success()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, CreateItemCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateItemCommand("Widget"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Send_Command_returning_ResultT_failure()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, FailingCreateItemCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateItemCommand("Widget"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("CreateFailed");
    }

    [Fact]
    public async Task Query_returning_ResultT_success()
    {
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<GetItemQuery, string>, GetItemQueryHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Query(new GetItemQuery(7));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Item-7");
    }

    [Fact]
    public async Task Stream_query_returns_all_items()
    {
        var services = new ServiceCollection();
        services.AddScoped<IStreamQueryHandler<GetNumbersQuery, int>, GetNumbersQueryHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.Stream(new GetNumbersQuery(5)))
        {
            items.Add(item);
        }

        items.ShouldBe([0, 1, 2, 3, 4]);
    }

    [Fact]
    public async Task Publish_invokes_all_handlers()
    {
        var handler1 = new OrderPlacedHandler1();
        var handler2 = new OrderPlacedHandler2();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderPlacedEvent(99));

        handler1.HandledOrderIds.ShouldBe([99]);
        handler2.HandledOrderIds.ShouldBe([99]);
    }

    [Fact]
    public async Task Publish_runs_all_handlers_even_when_some_fail()
    {
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
        failingHandler.WasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_command_handler_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new TestCommand("test")));

        ex.Message.ShouldContain("TestCommand");
        ex.Message.ShouldContain("ICommandHandler");
    }

    [Fact]
    public async Task Missing_query_handler_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Query(new GetItemQuery(1)));

        ex.Message.ShouldContain("GetItemQuery");
        ex.Message.ShouldContain("IQueryHandler");
    }

    [Fact]
    public void Missing_stream_handler_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = Should.Throw<InvalidOperationException>(
            () => mediator.Stream(new GetNumbersQuery(1)));

        ex.Message.ShouldContain("GetNumbersQuery");
        ex.Message.ShouldContain("IStreamQueryHandler");
    }

    [Fact]
    public async Task Implicit_conversion_TValue_to_ResultT_works_in_handler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, CreateItemCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // CreateItemCommandHandler returns Task.FromResult<Result<int>>(42) using implicit conversion
        var result = await mediator.Send(new CreateItemCommand("Widget"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Implicit_conversion_Error_to_Result_works_in_handler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, ErrorImplicitConversionCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("NotFound");
        result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Implicit_conversion_Error_to_ResultT_works_in_handler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, ErrorImplicitConversionCreateItemHandler>();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateItemCommand("Widget"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("Conflict");
        result.Errors[0].Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Send_with_CancellationToken_propagates_to_handler()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>>(_ =>
            new TokenCapturingHandler(ct => receivedToken = ct));
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"), cts.Token);

        receivedToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Publish_with_zero_handlers_succeeds_silently()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Should not throw
        await mediator.Publish(new OrderPlacedEvent(1));
    }

    [Fact]
    public async Task Missing_typed_command_handler_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new CreateItemCommand("test")));

        ex.Message.ShouldContain("CreateItemCommand");
        ex.Message.ShouldContain("ICommandHandler");
    }

    private class TokenCapturingHandler : ICommandHandler<TestCommand>
    {
        private readonly Action<CancellationToken> _capture;

        public TokenCapturingHandler(Action<CancellationToken> capture) => _capture = capture;

        public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
        {
            _capture(cancellationToken);
            return Task.FromResult(Result.Success());
        }
    }
}
