# C# Workflows Reference

## Contents
- Adding a Command Handler
- Adding a Query Handler
- Adding a Pipeline Behavior
- Adding a CLI Command
- Running and Filtering Tests
- Code Formatting and Validation

---

## Adding a Command Handler

Copy this checklist and track progress:

- [ ] Step 1: Define the command record with `: ICommand` or `: ICommand<TValue>`
- [ ] Step 2: Create the handler class — `sealed`, primary constructor, implements `ICommandHandler<TCmd, TValue>`
- [ ] Step 3: Create a FluentValidation validator (see the **fluent-validation** skill)
- [ ] Step 4: Build — source generator writes `AddModulusHandlers()` (no manual DI wiring)
- [ ] Step 5: Write tests in `Modulus.Mediator.Tests` using xUnit + Shouldly (see the **xunit** skill)

```csharp
// Step 1 — src/YourModule/Application/Commands/CreateUserCommand.cs
namespace YourModule.Application.Commands;

public record CreateUserCommand(string Email, string Name) : ICommand<Guid>;

// Step 2 — src/YourModule/Application/Commands/CreateUserHandler.cs
namespace YourModule.Application.Commands;

public sealed class CreateUserHandler(
    IUserRepository repo,
    IMessageBus bus)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        if (await repo.ExistsAsync(cmd.Email, ct))
            return Error.Conflict("User.Duplicate", "Email already registered");

        var user = new User(cmd.Email, cmd.Name);
        await repo.AddAsync(user, ct);
        await bus.PublishAsync(new UserCreatedEvent(user.Id, user.Email), ct);

        return user.Id;
    }
}
```

**Validate:** `dotnet build Modulus.slnx` — if MOD002 appears, the handler doesn't return `Result<T>`.

---

## Adding a Query Handler

Queries are read-only. The same record + sealed-handler pattern applies.

```csharp
// Query record
public record GetUserQuery(Guid UserId) : IQuery<UserDto>;

// Handler
public sealed class GetUserHandler(IUserRepository repo)
    : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetUserQuery query, CancellationToken ct)
    {
        var user = await repo.FindAsync(query.UserId, ct);
        return user is null
            ? Error.NotFound("User.NotFound", $"User {query.UserId} not found")
            : new UserDto(user.Id, user.Email, user.Name);
    }
}

// DTO — record for structural equality and immutability
public record UserDto(Guid Id, string Email, string Name);
```

Streaming queries implement `IStreamQuery<T>` and `IStreamQueryHandler<TQuery, T>`.
They **bypass the mediator pipeline entirely** — no validation, no logging behavior runs.
Use them only for large data exports where pipeline overhead is unacceptable.

---

## Adding a Pipeline Behavior

Behaviors run for every non-streaming request in registration order.
The built-in pipeline order is: `UnhandledExceptionBehavior → LoggingBehavior → ValidationBehavior → Handler`.

```csharp
// Custom metrics behavior — add BEFORE LoggingBehavior to make it outermost
public sealed class MetricsBehavior<TRequest, TResponse>(
    IMeterFactory meterFactory)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();
        // record metrics on response.IsSuccess
        return response;
    }
}

// Registration — order matters (first registered = outermost wrapper)
services.AddModulusMediator()
        .AddPipelineBehavior(typeof(MetricsBehavior<,>));
```

**Iterate-until-pass for behavior ordering:**
1. Register behavior
2. Run: `dotnet test Modulus.slnx --filter "FullyQualifiedName~BehaviorTests"`
3. If behavior order assertions fail, adjust registration order and repeat step 2

---

## Adding a CLI Command

CLI commands use a factory pattern — no DI container, dependencies passed explicitly for testability.

```csharp
// src/Modulus.Cli/Commands/GreetCommand.cs
namespace Modulus.Cli.Commands;

public static class GreetCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var nameArg = new Argument<string>("name") { Description = "Name to greet" };

        var command = new Command("greet", "Say hello")
        {
            nameArg,
        };

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg)!;

            if (!CSharpIdentifierValidator.IsValid(name))
            {
                console.WriteError($"'{name}' is not a valid C# identifier.");
                return Task.FromResult(1);
            }

            console.WriteLine($"Hello, {name}!");
            return Task.FromResult(0);
        });

        return command;
    }
}
```

Register in `src/Modulus.Cli/Program.cs`:

```csharp
rootCommand.AddCommand(GreetCommand.Create(fileSystem, console));
```

Test with `FakeConsole` and `FakeFileSystem` — no real file I/O in unit tests (see the **xunit** skill):

```csharp
[Fact]
public async Task GreetCommand_ValidName_WritesHello()
{
    var console = new FakeConsoleOutput();
    var fs = new FakeFileSystem();

    var command = GreetCommand.Create(fs, console);
    var exitCode = await command.InvokeAsync("Alice");

    exitCode.ShouldBe(0);
    console.Output.ShouldContain("Hello, Alice!");
}
```

---

## Running and Filtering Tests

```powershell
# All tests
dotnet test Modulus.slnx

# Only mediator tests
dotnet test Modulus.slnx --filter "FullyQualifiedName~Mediator"

# Only CLI tests
dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"

# Specific class
dotnet test Modulus.slnx --filter "FullyQualifiedName~ResultTests"

# Only async tests
dotnet test Modulus.slnx --filter "Name~Async"
```

**Iterate-until-pass for all tests:**
1. Write handler + validator + test
2. `dotnet build Modulus.slnx` — fix any compiler/analyzer errors
3. `dotnet test Modulus.slnx --filter "FullyQualifiedName~YourTest"`
4. Fix failures, repeat step 3 until green

---

## Code Formatting and Validation

```powershell
# Format all files per .editorconfig rules
dotnet format Modulus.slnx

# Check format without changing files (CI usage)
dotnet format Modulus.slnx --verify-no-changes

# Build to trigger Roslyn analyzer warnings (MOD001-MOD005)
dotnet build Modulus.slnx
```

`.editorconfig` enforces:
- File-scoped namespaces (warning if missing)
- `_camelCase` private fields (suggestion)
- PascalCase for classes, methods, interfaces (warning/suggestion)
- 4-space indent for `.cs`, 2-space for `.csproj`/`.json`/`.yml`

Run `dotnet format` before committing — CI will fail on format violations.
