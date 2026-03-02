using FluentValidation;

namespace Modulus.Mediator.Tests.Fixtures;

public class TestCommandNameValidator : AbstractValidator<TestCommand>
{
    public TestCommandNameValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
    }
}

public class TestCommandNameLengthValidator : AbstractValidator<TestCommand>
{
    public TestCommandNameLengthValidator()
    {
        RuleFor(x => x.Name).MaximumLength(5).WithMessage("Name must not exceed 5 characters");
    }
}

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
    }
}
