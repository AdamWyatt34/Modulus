# xUnit Patterns Reference

## Contents
- Test Structure & Naming
- Shouldly Assertions
- Fakes vs Mocks
- Pipeline Behavior Testing
- Generator & Analyzer Testing
- Anti-Patterns

---

## Test Structure & Naming

Convention: `Method_Scenario_Expected`

```csharp
// ✅ Good
public async Task Send_Command_ReturnsSuccess()
public async Task Send_InvalidCommand_ReturnsValidationError()
public async Task Init_with_invalid_name_returns_error()
public void Error_factory_methods_set_correct_type(ErrorType expectedType)

// ❌ Bad — vague, no scenario, no expected
public async Task TestSend()
public void TestError()
```

Each test owns its own `ServiceCollection`. NEVER share a `ServiceProvider` across tests — DI state leaks cause intermittent failures that are nearly impossible to diagnose.

```csharp
// ✅ Inline DI per test
var services = new ServiceCollection();
services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
services.AddScoped<IMediator, Mediator>();
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
```

---

## Shouldly Assertions

Prefer Shouldly over raw `Assert.*` — failure messages include the actual value automatically.

```csharp
// Result pattern assertions
result.IsSuccess.ShouldBeTrue();
result.IsFailure.ShouldBeTrue();
result.Errors.ShouldBeEmpty();
result.Errors.Count.ShouldBe(1);
result.Errors[0].Code.ShouldBe("NotFound");
result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
result.Value.ShouldBe(42);

// Collection assertions
items.ShouldBe([0, 1, 2, 3, 4]);
_proc.Invocations.ShouldContain(i => i.Command == "git" && i.Arguments == "init");
_proc.Invocations.ShouldNotContain(i => i.Command == "git");

// Type assertions
result.ShouldBeOfType<ValidationResult>();
result.ShouldBeAssignableTo<Result>();

// String assertions
ex.Message.ShouldContain("TestCommand");
slnxContent.ShouldContain("AppHost");

// Exception assertions — use Should.ThrowAsync, NOT Assert.ThrowsAsync
var ex = await Should.ThrowAsync<InvalidOperationException>(
    () => mediator.Send(new TestCommand("test")));
ex.Message.ShouldContain("ICommandHandler");

// Synchronous throw
var ex = Should.Throw<InvalidOperationException>(
    () => mediator.Stream(new GetNumbersQuery(1)));
```

---

## Fakes vs Mocks

NEVER use Moq or NSubstitute. The project mandates hand-written `Fake*` test doubles in `tests/*/Fakes/`.

**Why:** Fakes make behavior explicit and readable. Mock frameworks hide setup complexity in fluent chains that obscure what the test is actually verifying.

```csharp
// ✅ FakeFileSystem — explicit, inspectable
_fs.SeedFile(@"C:\work\EShop\existing.txt", "content");
_fs.FileExists(@"C:\work\EShop\EShop.slnx").ShouldBeTrue();
var content = _fs.ReadAllText(@"C:\work\EShop\appsettings.json");
content.ShouldContain("RabbitMq");

// ✅ FakeProcessRunner — records invocations
_proc.Invocations.ShouldContain(i => i.Command == "dotnet" && i.Arguments == "restore");

// ✅ FakeConsole — captures output streams
_console.ErrorLines.ShouldContain(l => l.Contains("already exists"));

// ❌ DON'T do this
var mockFs = new Mock<IFileSystem>();
mockFs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
```

For one-off behavior in mediator tests, use private inner classes directly in the test file:

```csharp
private class TrackingCommandHandler : ICommandHandler<TestCommand>
{
    private readonly Action _onHandle;
    public TrackingCommandHandler(Action onHandle) => _onHandle = onHandle;
    public Task<Result> Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        _onHandle();
        return Task.FromResult(Result.Success());
    }
}
```

---

## Pipeline Behavior Testing

Register behaviors with `AddPipelineBehavior(typeof(BehaviorType<,>))`. The order of registration = outermost first.

```csharp
[Fact]
public async Task Behaviors_execute_in_registration_order()
{
    var executionLog = new List<string>();
    var services = new ServiceCollection();
    services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
    services.AddScoped<IMediator, Mediator>();
    services.AddSingleton(executionLog);        // shared via DI
    services.AddPipelineBehavior(typeof(RecordingBehavior1<,>));
    services.AddPipelineBehavior(typeof(RecordingBehavior2<,>));
    // ...

    executionLog.ShouldBe([
        "Behavior1-Before",
        "Behavior2-Before",
        "Behavior2-After",
        "Behavior1-After"
    ]);
}
```

Use `[Theory] [InlineData]` to verify behavior across multiple error types rather than duplicating test methods:

```csharp
[Theory]
[InlineData(ErrorType.Failure)]
[InlineData(ErrorType.Validation)]
[InlineData(ErrorType.NotFound)]
[InlineData(ErrorType.Conflict)]
[InlineData(ErrorType.Unauthorized)]
[InlineData(ErrorType.Forbidden)]
public void Error_factory_methods_set_correct_type(ErrorType expectedType) { ... }
```

---

## Generator & Analyzer Testing

Source generator tests use `GeneratorTestHelper.RunHandlerRegistrationGenerator`. Always verify two things:
1. Generated source content (`ShouldContain`)
2. Zero compilation errors

```csharp
var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");
var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

// Check generated content
generated.ShouldContain("// Commands");
generated.ShouldContain("services.AddScoped<global::Modulus.Mediator.Abstractions.ICommandHandler<...");

// Always check compilation errors
outputCompilation.GetDiagnostics()
    .Where(d => d.Severity == DiagnosticSeverity.Error)
    .ShouldBeEmpty();
```

For end-to-end generator verification, emit the compilation and resolve from `ServiceProvider`:

```csharp
using var ms = new MemoryStream();
var emitResult = outputCompilation.Emit(ms);
emitResult.Success.ShouldBeTrue();

ms.Seek(0, SeekOrigin.Begin);
var assembly = Assembly.Load(ms.ToArray());
var registrationClass = assembly.GetType("TestApp.ModulusHandlerRegistrations");
registrationClass.ShouldNotBeNull();
```

---

## WARNING: Anti-Patterns

### WARNING: Sharing ServiceProvider Across Tests

**The Problem:**
```csharp
// BAD — static shared provider
private static readonly IServiceProvider _provider = BuildProvider();

[Fact] public async Task Test1() { /* mutates singleton state */ }
[Fact] public async Task Test2() { /* sees Test1's state */ }
```

**Why This Breaks:** xUnit runs `[Fact]` methods in parallel by default within a class. Shared DI state causes race conditions and ordering-dependent failures.

**The Fix:** Build a fresh `ServiceCollection` in each test.

---

### WARNING: Testing Value from Failed Result

**The Problem:**
```csharp
// BAD — throws InvalidOperationException
var result = Result<int>.Failure(Error.Failure("F", "err"));
result.Value.ShouldBe(0); // throws before assertion runs
```

**Why This Breaks:** `Result<T>.Value` throws when `IsFailure`. The test fails with an exception rather than a Shouldly assertion message, making failures harder to diagnose.

**The Fix:** Assert `IsFailure` first, then assert errors. Never access `.Value` on a failure result.

```csharp
result.IsFailure.ShouldBeTrue();
result.Errors[0].Code.ShouldBe("F");
```

---

### WARNING: Using Assert.ThrowsAsync Instead of Should.ThrowAsync

**The Problem:**
```csharp
// BAD — doesn't work correctly with async void edge cases
await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(...));
```

**The Fix:**
```csharp
var ex = await Should.ThrowAsync<InvalidOperationException>(() => mediator.Send(...));
ex.Message.ShouldContain("expected text");
```

`Should.ThrowAsync` returns the exception for further assertions, which `Assert.ThrowsAsync` does not chain as cleanly with Shouldly.
