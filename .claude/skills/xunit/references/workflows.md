# xUnit Workflows Reference

## Contents
- Adding a New Test
- Adding a Test Fixture
- Adding a Fake Test Double
- Testing a New CLI Handler
- Testing a New Source Generator
- Running Tests
- Checklist: New Test Class

---

## Adding a New Test

1. Identify the test project:
   - Mediator pipeline/Result → `tests/Modulus.Mediator.Tests/`
   - CLI handler logic → `tests/Modulus.Cli.Tests/`
   - Messaging outbox/inbox → `tests/Modulus.Messaging.Tests/`
   - Roslyn source generators → `tests/Modulus.Generators.Tests/`
   - Roslyn analyzers → `tests/Modulus.Analyzers.Tests/`

2. Match the folder structure to the production namespace. E.g., `Modulus.Mediator.Behaviors` tests go in `tests/Modulus.Mediator.Tests/Behaviors/`.

3. Use file-scoped namespace: `namespace Modulus.Mediator.Tests.Behaviors;`

```csharp
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Behaviors;

public class MetricsBehaviorTests
{
    [Fact]
    public async Task Records_metrics_on_success()
    {
        // Arrange
        var services = new ServiceCollection();
        // ...

        // Act
        var result = await mediator.Send(new TestCommand("test"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
```

---

## Adding a Test Fixture

Reusable test commands, queries, and handlers live in `tests/*/Fixtures/`. Keep fixtures minimal — only what multiple test classes actually need.

```csharp
// tests/Modulus.Mediator.Tests/Fixtures/TestCommand.cs
namespace Modulus.Mediator.Tests.Fixtures;

public record TestCommand(string Name) : ICommand;

public class TestCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());
}

public class FailingTestCommandHandler : ICommandHandler<TestCommand>
{
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(Error.Failure("TestError", "Something went wrong")));
}
```

One-off handlers used only within a single test class belong as `private class` inside that test class, not in `Fixtures/`.

---

## Adding a Fake Test Double

Create new fakes in `tests/Modulus.Cli.Tests/Fakes/` implementing an infrastructure interface from `src/Modulus.Cli/Infrastructure/`.

```csharp
// tests/Modulus.Cli.Tests/Fakes/FakeConsole.cs
namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeConsole : IConsoleOutput
{
    private readonly List<string> _lines = [];
    private readonly List<string> _errorLines = [];

    public IReadOnlyList<string> Lines => _lines;
    public IReadOnlyList<string> ErrorLines => _errorLines;

    public void WriteLine(string message) => _lines.Add(message);
    public void WriteError(string message) => _errorLines.Add(message);
}
```

Pattern: fakes record invocations or state that tests can inspect via public read-only properties.

---

## Testing a New CLI Handler

Copy this checklist and track progress:
- [ ] Create handler under `src/Modulus.Cli/Handlers/MyCommandHandler.cs`
- [ ] Create test class under `tests/Modulus.Cli.Tests/Handlers/MyCommandHandlerTests.cs`
- [ ] Wire up `FakeFileSystem`, `FakeProcessRunner`, `FakeConsole` in test class fields
- [ ] Add factory method `private MyCommandHandler CreateHandler() => new(_fs, _proc, _console);`
- [ ] Write success-path test (exit code 0, verify key files created)
- [ ] Write validation-failure test (exit code 1, verify error output)
- [ ] Write precondition-failure test (e.g., directory already exists)
- [ ] Verify process invocations where applicable

```csharp
public class AddModuleHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeConsole _console = new();

    private AddModuleHandler CreateHandler() => new(_fs, _proc, _console);

    [Fact]
    public async Task AddModule_creates_expected_structure()
    {
        // Seed a pre-existing solution
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<solution />");

        var result = await CreateHandler().ExecuteAsync("Orders", @"C:\work\EShop");

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Orders.Domain\Orders.Domain.csproj").ShouldBeTrue();
    }

    [Fact]
    public async Task AddModule_with_invalid_name_returns_error()
    {
        var result = await CreateHandler().ExecuteAsync("123Bad", @"C:\work\EShop");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }
}
```

Iterate-until-pass:
1. Run `dotnet test Modulus.slnx --filter "FullyQualifiedName~AddModuleHandler"`
2. If tests fail, fix the handler or assertions
3. Repeat until all pass before moving on

---

## Testing a New Source Generator

Copy this checklist and track progress:
- [ ] Write source string representing the C# types the generator will scan
- [ ] Call `GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp")`
- [ ] Call `GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs")`
- [ ] Assert expected `services.AddScoped<...>` registrations appear in generated source
- [ ] Assert zero `DiagnosticSeverity.Error` in `outputCompilation.GetDiagnostics()`
- [ ] For edge cases (open generics, no handlers), assert negative cases with `ShouldNotContain`

```csharp
[Fact]
public void Generate_IntegrationEventHandler_RegistersCorrectly()
{
    var source = SystemUsings + """
        using Modulus.Mediator.Abstractions;
        using Modulus.Messaging.Abstractions;

        namespace TestApp;

        public record OrderShippedEvent : IIntegrationEvent
        {
            public Guid EventId { get; } = Guid.NewGuid();
            public DateTime OccurredOn { get; } = DateTime.UtcNow;
            public string? CorrelationId { get; }
        }

        public sealed class OrderShippedHandler : IIntegrationEventHandler<OrderShippedEvent>
        {
            public Task Handle(OrderShippedEvent @event, CancellationToken ct = default)
                => Task.CompletedTask;
        }
        """;

    var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");
    var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

    generated.ShouldContain("// Integration Events");
    generated.ShouldContain("IIntegrationEventHandler<global::TestApp.OrderShippedEvent>, global::TestApp.OrderShippedHandler>");
    outputCompilation.GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .ShouldBeEmpty();
}
```

---

## Running Tests

```powershell
# All tests
dotnet test Modulus.slnx

# Single test project
dotnet test tests/Modulus.Mediator.Tests/Modulus.Mediator.Tests.csproj

# Filter by class
dotnet test Modulus.slnx --filter "FullyQualifiedName~MediatorTests"

# Filter by name pattern
dotnet test Modulus.slnx --filter "Name~Async"

# Specific test project category
dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"
dotnet test Modulus.slnx --filter "FullyQualifiedName~Generators"
dotnet test Modulus.slnx --filter "FullyQualifiedName~Analyzers"
```

---

## Checklist: New Test Class

Copy and track for every new test class:

- [ ] File-scoped namespace matching folder path
- [ ] Using order: System → external (Shouldly, Xunit) → internal (Modulus.*)
- [ ] Class is `public` (xUnit discovers `public` test classes only)
- [ ] Test method names follow `Method_Scenario_Expected`
- [ ] Each `[Fact]` builds its own `ServiceCollection` (no shared provider)
- [ ] Assertions use Shouldly, not `Assert.*`
- [ ] Exception assertions use `Should.ThrowAsync<T>` / `Should.Throw<T>`
- [ ] No Moq/NSubstitute — use `Fake*` doubles or private inner handler classes
- [ ] Run tests locally and verify all pass before committing

See the **csharp** skill for naming and namespace conventions.
See the **fluent-validation** skill for validator fixture patterns used in `ValidationBehaviorTests`.
