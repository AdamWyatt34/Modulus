using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public class LoggingBehaviorTests
{
    [Fact]
    public async Task Logs_request_name_and_success()
    {
        var logger = new TestLogger<LoggingBehavior<TestCommand, Result>>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<LoggingBehavior<TestCommand, Result>>>(logger);
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        logger.Entries.Count.ShouldBe(2);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldContain("TestCommand");
        logger.Entries[1].Level.ShouldBe(LogLevel.Information);
        logger.Entries[1].Message.ShouldContain("successfully");
        logger.Entries[1].Message.ShouldContain("ms");
    }

    [Fact]
    public async Task Logs_failure_with_error_codes()
    {
        var logger = new TestLogger<LoggingBehavior<TestCommand, Result>>();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, FailingTestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<ILogger<LoggingBehavior<TestCommand, Result>>>(logger);
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        logger.Entries.Count.ShouldBe(2);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldContain("Handling");
        logger.Entries[1].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[1].Message.ShouldContain("failure");
        logger.Entries[1].Message.ShouldContain("TestError");
    }
}
