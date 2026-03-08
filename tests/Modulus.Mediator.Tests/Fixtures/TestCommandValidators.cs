using FluentValidation;

namespace Modulus.Mediator.Tests.Fixtures;

/// <summary>
/// Validates that TestCommand.Name is not empty.
/// Produces an error with code "Name".
/// </summary>
public class TestCommandNameValidator : AbstractValidator<TestCommand>
{
    public TestCommandNameValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithName("Name");
    }
}

/// <summary>
/// Validates that TestCommand.Name meets a minimum length of 10 characters.
/// Used in combination with TestCommandNameValidator to produce multiple errors
/// (e.g., empty string triggers both NotEmpty and MinimumLength).
/// </summary>
public class TestCommandNameLengthValidator : AbstractValidator<TestCommand>
{
    public TestCommandNameLengthValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(10)
            .WithName("Name");
    }
}

/// <summary>
/// Validates that CreateItemCommand.Name is not empty.
/// Produces an error with code "Name".
/// </summary>
public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithName("Name");
    }
}
