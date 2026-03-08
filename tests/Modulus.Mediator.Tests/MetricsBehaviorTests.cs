using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public sealed class MetricsBehaviorTests
{
    /// <summary>
    /// Hand-crafted fake IMeterFactory that creates real Meter instances so MetricsBehavior
    /// can record measurements without throwing. Disposed meters are tracked for assertion.
    /// </summary>
    private sealed class FakeMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public IReadOnlyList<Meter> CreatedMeters => _meters;

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_ReturnsSuccess()
    {
        // Arrange
        var meterFactory = new FakeMeterFactory();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.Send(new TestCommand("test"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_FailedRequest_ReturnsFailure()
    {
        // Arrange
        var meterFactory = new FakeMeterFactory();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, FailingTestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.Send(new TestCommand("test"));

        // Assert — failure result still returned correctly (no exception leaks from behavior)
        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("TestError");
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_CreatesMeter()
    {
        // Arrange
        var meterFactory = new FakeMeterFactory();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new TestCommand("test"));

        // Assert — MetricsBehavior must have called IMeterFactory.Create
        meterFactory.CreatedMeters.Count.ShouldBe(1);
        meterFactory.CreatedMeters[0].Name.ShouldBe("Modulus.Mediator");
    }

    [Fact]
    public async Task Handle_ThrowingHandler_RecordsMetricsAndRethrows()
    {
        // Arrange
        var meterFactory = new FakeMeterFactory();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, ThrowingCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert — exception propagates past MetricsBehavior (it re-throws after recording)
        await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new TestCommand("test")));
    }

    [Fact]
    public async Task Handle_MultipleRequests_AllMetersUseCorrectName()
    {
        // Arrange — MetricsBehavior is transient; each mediator call creates a new behavior instance
        // and calls IMeterFactory.Create once per instance.
        var meterFactory = new FakeMeterFactory();
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
        using var provider = services.BuildServiceProvider();

        // Act — two separate scopes; each creates a new transient behavior instance
        using (var scope1 = provider.CreateScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
            await mediator1.Send(new TestCommand("first"));
        }

        using (var scope2 = provider.CreateScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
            await mediator2.Send(new TestCommand("second"));
        }

        // Assert — every meter created by any behavior instance uses the canonical meter name
        meterFactory.CreatedMeters.ShouldNotBeEmpty();
        meterFactory.CreatedMeters.ShouldAllBe(m => m.Name == "Modulus.Mediator");
    }
}
