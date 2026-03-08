using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public class UnhandledExceptionBehaviorTests
{
    [Fact]
    public async Task Catches_exception_and_returns_Result_Failure()
    {
        var logger = new TestLogger<UnhandledExceptionBehavior<TestCommand, Result>>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, ThrowingCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<UnhandledExceptionBehavior<TestCommand, Result>>>(logger);
        services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("UnhandledException");
        result.Errors[0].Description.ShouldBe("An unexpected error occurred.");
    }

    [Fact]
    public async Task Logs_the_exception()
    {
        var logger = new TestLogger<UnhandledExceptionBehavior<TestCommand, Result>>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, ThrowingCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<UnhandledExceptionBehavior<TestCommand, Result>>>(logger);
        services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error);
        logger.Entries.ShouldContain(e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Passes_through_on_success()
    {
        var logger = new TestLogger<UnhandledExceptionBehavior<TestCommand, Result>>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<UnhandledExceptionBehavior<TestCommand, Result>>>(logger);
        services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Catches_exception_and_returns_ResultT_Failure()
    {
        // Create a handler that throws for ICommand<int>
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, ThrowingCreateItemHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<UnhandledExceptionBehavior<CreateItemCommand, Result<int>>>>(
            new TestLogger<UnhandledExceptionBehavior<CreateItemCommand, Result<int>>>());
        services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateItemCommand("test"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("UnhandledException");
    }

    private class ThrowingCreateItemHandler : ICommandHandler<CreateItemCommand, int>
    {
        public Task<Result<int>> Handle(CreateItemCommand command, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler exploded");
        }
    }
}
