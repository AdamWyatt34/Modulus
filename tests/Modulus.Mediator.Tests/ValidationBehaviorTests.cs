using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Returns_ValidationResult_with_errors_when_validation_fails()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<TestCommand>, TestCommandNameValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Empty name should fail validation
        var result = await mediator.Send(new TestCommand(""));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<ValidationResult>();
        result.Errors.ShouldContain(e => e.Code == "Name" && e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_ValidationResultT_with_errors_for_typed_command()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CreateItemCommand, int>, CreateItemCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<CreateItemCommand>, CreateItemCommandValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateItemCommand(""));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<ValidationResult<int>>();
        result.Errors.ShouldContain(e => e.Code == "Name" && e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Calls_next_when_no_validators_registered()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        // No validators registered
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("valid"));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Calls_next_when_all_validators_pass()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<TestCommand>, TestCommandNameValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("valid"));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Collects_errors_from_multiple_validators()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<TestCommand>, TestCommandNameValidator>();
        services.AddScoped<IValidator<TestCommand>, TestCommandNameLengthValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Empty string triggers both validators:
        //   TestCommandNameValidator  → NotEmpty fires (error code "Name")
        //   TestCommandNameLengthValidator → MinimumLength(10) fires (error code "Name")
        var result = await mediator.Send(new TestCommand(""));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<ValidationResult>();
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Validation_failure_does_not_throw_exceptions()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<TestCommand>, TestCommandNameValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // This should NOT throw - validation failures are returned as data
        var result = await mediator.Send(new TestCommand(""));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Returns_ValidationResultT_for_query_with_validation_failure()
    {
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<GetItemQuery, string>, GetItemQueryHandler>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IValidator<GetItemQuery>, GetItemQueryValidator>();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Query(new GetItemQuery(-1));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<ValidationResult<string>>();
        result.Errors.ShouldContain(e => e.Type == ErrorType.Validation);
    }
}

public class GetItemQueryValidator : AbstractValidator<GetItemQuery>
{
    public GetItemQueryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be positive");
    }
}
