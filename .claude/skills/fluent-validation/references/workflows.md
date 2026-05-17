# FluentValidation Workflows Reference

## Contents
- Adding a validator end-to-end
- Testing validators and ValidationBehavior
- Debugging validation failures
- Multiple validators per request

---

## Adding a Validator End-to-End

Copy this checklist and track progress:

- [ ] Step 1: Create the validator class in the same folder as the command/query
- [ ] Step 2: Inherit `AbstractValidator<TRequest>` and add rules in the constructor
- [ ] Step 3: Build — source generator registers it automatically
- [ ] Step 4: Verify registration in `obj/**/ModulusHandlerRegistrations.g.cs`
- [ ] Step 5: Write unit tests covering failure and success paths
- [ ] Step 6: Run tests

```powershell
# After writing the validator, build and run tests
dotnet build Modulus.slnx
dotnet test Modulus.slnx --filter "FullyQualifiedName~MyModule"
```

Validator is live once the build succeeds. No DI wiring needed.

---

## Testing Validators and ValidationBehavior

Test through the mediator pipeline, not by invoking the validator directly. This validates that `ValidationBehavior` is wired correctly and that error mapping produces the expected `Error` objects.

### Failure Path

```csharp
[Fact]
public async Task Handle_InvalidCommand_ReturnsValidationErrors()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddScoped<ICommandHandler<CreateOrderCommand>, CreateOrderHandler>();
    services.AddScoped<IMediator, Mediator>();
    services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
    services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
    using var provider = services.BuildServiceProvider();
    var mediator = provider.CreateScope().ServiceProvider.GetRequiredService<IMediator>();

    var command = new CreateOrderCommand(CustomerId: Guid.Empty, Items: []);

    // Act
    var result = await mediator.Send(command);

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.ShouldBeOfType<ValidationResult>();
    result.Errors.ShouldContain(e => e.Code == "CustomerId" && e.Type == ErrorType.Validation);
    result.Errors.ShouldContain(e => e.Code == "Items" && e.Type == ErrorType.Validation);
}
```

### Success Path (No Validators Registered)

```csharp
[Fact]
public async Task Handle_NoValidators_CallsHandler()
{
    var services = new ServiceCollection();
    services.AddScoped<ICommandHandler<CreateOrderCommand>, CreateOrderHandler>();
    services.AddScoped<IMediator, Mediator>();
    // Deliberately no validator registered
    services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
    using var provider = services.BuildServiceProvider();
    var mediator = provider.CreateScope().ServiceProvider.GetRequiredService<IMediator>();

    var result = await mediator.Send(new CreateOrderCommand(Guid.NewGuid(), [new LineItem(...)]));

    result.IsSuccess.ShouldBeTrue();
}
```

### Typed Result (IQuery<T>)

```csharp
[Fact]
public async Task Handle_InvalidQuery_ReturnsValidationResultT()
{
    // Setup as above, validator for GetOrderQuery with Id > 0 rule
    var result = await mediator.Send(new GetOrderQuery(Id: -1));

    result.IsFailure.ShouldBeTrue();
    result.ShouldBeOfType<ValidationResult<Order>>();
    result.Errors.ShouldContain(e => e.Code == "Id");
}
```

See the **xunit** skill for test project setup, fixture patterns, and Shouldly assertion conventions.

---

## Debugging Validation Failures

**Symptom:** Request fails but you're not sure which validator fired.

1. Inspect `result.Errors` — each `Error.Code` is the property name from the `RuleFor` lambda.
2. Check `result.Errors[i].Description` for the FluentValidation message.
3. If errors are empty but `IsFailure` is true, the handler returned `Error.*` — not a validator.

**Symptom:** Validator not running at all — handler executes without validation.

Iterate until pass:
1. Check `ValidationBehavior<,>` is registered: `services.AddPipelineBehavior(typeof(ValidationBehavior<,>))`
2. Check validator is registered: search `obj/**/ModulusHandlerRegistrations.g.cs` for your validator name
3. Rebuild: `dotnet build Modulus.slnx`
4. If still missing, verify the class inherits `AbstractValidator<T>` with a concrete `T` (not open generic)
5. Re-run tests

```powershell
# Check generated file for your validator
Select-String -Path ".\**\ModulusHandlerRegistrations.g.cs" `
    -Pattern "CreateOrderCommandValidator" -Recurse
```

**Symptom:** Duplicate validation errors (each rule fires twice).

Cause: validator registered both by source generator and manually. Remove the manual `services.AddScoped<IValidator<T>, ...>()` call.

---

## Multiple Validators Per Request

`ValidationBehavior` resolves `IEnumerable<IValidator<TRequest>>` and runs all concurrently. Errors from all validators aggregate into a single `ValidationResult`.

Use multiple validators when structural and business-rule validation are distinct enough to separate:

```csharp
// Structural: shape of the input
public sealed class CreateOrderStructuralValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderStructuralValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new LineItemValidator());
    }
}

// Business rules: domain-specific constraints (no DB calls)
public sealed class CreateOrderBusinessValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderBusinessValidator()
    {
        RuleFor(x => x.Items)
            .Must(items => items.Sum(i => i.Quantity) <= 500)
            .WithMessage("Total quantity cannot exceed 500 units per order.");
    }
}
```

Both are discovered and registered automatically. Errors from both appear in `result.Errors` when the request is invalid.

Test aggregation explicitly:

```csharp
[Fact]
public async Task Handle_InvalidCommand_CollectsErrorsFromMultipleValidators()
{
    // register both validators
    services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderStructuralValidator>();
    services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderBusinessValidator>();

    var result = await mediator.Send(new CreateOrderCommand(Guid.Empty, TooManyItems));

    result.Errors.ShouldContain(e => e.Code == "CustomerId");   // from structural
    result.Errors.ShouldContain(e => e.Code == "Items");        // from business
}
```
