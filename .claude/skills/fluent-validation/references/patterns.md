# FluentValidation Patterns Reference

## Contents
- Validator structure and naming
- Error mapping: FluentValidation → Modulus Result
- DO/DON'T pairs
- Anti-patterns
- Source generator discovery rules

---

## Validator Structure and Naming

Name validators `{RequestName}Validator`. The source generator detects any class inheriting `AbstractValidator<T>` regardless of naming, but this convention is enforced by team practice.

```csharp
namespace MyModule.Application.Commands;

// GOOD — sealed, primary constructor, file-scoped namespace
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.TotalAmount).GreaterThan(0);
        RuleForEach(x => x.LineItems).SetValidator(new LineItemValidator());
    }
}
```

Child validators for nested objects follow the same pattern:

```csharp
public sealed class LineItemValidator : AbstractValidator<LineItem>
{
    public LineItemValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999);
        RuleFor(x => x.UnitPrice).GreaterThan(0m);
    }
}
```

---

## Error Mapping: FluentValidation → Modulus Result

`ValidationBehavior` maps `ValidationFailure` to `Error` using:
- `failure.PropertyName` → `Error.Code`
- `failure.ErrorMessage` → `Error.Description`
- `ErrorType.Validation` always

The returned type is `ValidationResult` (for `ICommand`) or `ValidationResult<T>` (for `ICommand<T>` / `IQuery<T>`). `ResultFactory` uses reflection to construct the correct closed generic at runtime (`src/Modulus.Mediator/Internals/ResultFactory.cs`).

```csharp
// What the behavior returns (conceptually):
Error.Validation("CustomerId", "'Customer Id' must not be empty.")
Error.Validation("TotalAmount", "'Total Amount' must be greater than '0'.")

// Callers check errors like this:
var result = await mediator.Send(command);
if (result.IsFailure)
{
    var validationErrors = result.Errors.Where(e => e.Type == ErrorType.Validation);
    // map to HTTP 400 / problem details
}
```

Set explicit property names and messages to control error codes:

```csharp
RuleFor(x => x.Email)
    .NotEmpty().WithName("email")          // Error.Code = "email"
    .EmailAddress().WithMessage("Invalid email format.");
```

---

## DO/DON'T Pairs

**DO** use `sealed` on validators — they are never subclassed, and the source generator registers them as scoped services.

```csharp
// GOOD
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> { }

// BAD — unnecessary inheritance surface
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> { }
```

**DO** place validators in the same namespace/folder as their request. The source generator scans the entire assembly, but co-location makes them trivially discoverable.

**DON'T** inject dependencies into validators for database-level uniqueness checks. `ValidationBehavior` runs before the handler; DB state belongs in the handler using the Result pattern.

```csharp
// BAD — validator doing DB work
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator(IOrderRepository repo)
    {
        RuleFor(x => x.CustomerId)
            .MustAsync(async (id, ct) => await repo.CustomerExistsAsync(id, ct));
    }
}

// GOOD — structural validation only in validator, existence check in handler
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
    }
}

// In handler:
var customer = await _repo.FindCustomerAsync(command.CustomerId, ct);
if (customer is null)
    return Error.NotFound("Customer.NotFound", $"Customer {command.CustomerId} not found.");
```

**DON'T** throw exceptions from validators. The pipeline returns `ValidationResult` — exceptions bypass that and break the Result contract.

---

## WARNING: Registering Validators Manually

**The Problem:**

```csharp
// BAD — manual registration
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

**Why This Breaks:**
1. The source generator already emits this registration in `ModulusHandlerRegistrations.g.cs` — you get duplicate registrations, causing `IEnumerable<IValidator<T>>` to run the validator twice.
2. If you rename the validator, the manual registration goes stale silently.

**The Fix:**

```csharp
// GOOD — rely solely on the source generator
services.AddModulusHandlers(); // contains all validators
```

---

## WARNING: Async Validators with Injected Services

**The Problem:**

```csharp
// BAD — forces DI injection, encourages DB calls in validators
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator(IProductCatalog catalog)
    {
        RuleFor(x => x.ProductId)
            .MustAsync((id, ct) => catalog.ExistsAsync(id, ct));
    }
}
```

**Why This Breaks:**
1. Existence checks belong in the handler where you return `Error.NotFound` — not in validation.
2. Mixes structural validation (input shape) with business rules (domain state).
3. Makes validators harder to unit-test without setting up full infrastructure fakes.

**The Fix:** Keep validators structurally focused. Move existence/uniqueness checks to the handler and return `Error.NotFound` or `Error.Conflict`.

---

## Source Generator Discovery Rules

The generator (`src/Modulus.Generators/HandlerRegistrationGenerator.cs`) walks the base type chain looking for `AbstractValidator<T>`. Rules:

- Class must be in the compiled assembly (not a referenced package)
- Class need not be `public` — but `internal` validators work fine
- Generic validators are **not** supported — `AbstractValidator<T>` with open `T` is skipped
- Rebuild required after adding a new validator — generators run at compile time

Generated output in `ModulusHandlerRegistrations.g.cs`:

```csharp
// Validators
services.AddScoped<global::FluentValidation.IValidator<global::MyModule.CreateOrderCommand>,
    global::MyModule.CreateOrderCommandValidator>();
```

Verify discovery by searching the generated file:

```powershell
dotnet build Modulus.slnx
Select-String -Path "obj\**\ModulusHandlerRegistrations.g.cs" -Pattern "CreateOrderCommandValidator" -Recurse
```
