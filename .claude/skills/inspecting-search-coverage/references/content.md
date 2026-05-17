# Content Reference — XML Documentation & IntelliSense Coverage

## Contents
- Enabling XML doc generation
- Public API coverage priorities
- XML doc patterns for this codebase
- Anti-patterns

## Enabling XML Doc Generation

Add to `Directory.Build.props` to enforce XML docs across all packages:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <!-- Warn on missing docs for public API — treat as error in CI -->
  <NoWarn>$(NoWarn)</NoWarn>
</PropertyGroup>
```

To surface gaps without failing the build during development:

```powershell
dotnet build Modulus.slnx /p:GenerateDocumentationFile=true 2>&1 | Select-String "CS1591"
```

CS1591 fires for every public member missing a `<summary>`. This is your coverage audit tool.

## Priority: Abstractions First

IntelliSense docs matter most on the types consumers depend on. In ModulusKit:

1. **`Modulus.Mediator.Abstractions`** — highest priority (`ICommand`, `IQuery`, `Result<T>`, `Error`)
2. **`Modulus.Mediator`** — DI extensions (`AddModulusMediator`)
3. **`Modulus.Messaging.Abstractions`** — `IIntegrationEvent`, `IMessageBus`
4. **`Modulus.Analyzers`** — diagnostic IDs (MOD001–MOD005)

Source generators and CLI internals are lower priority.

## XML Doc Patterns for This Codebase

### Interfaces (highest value)

```csharp
/// <summary>
/// Marks a request that returns <see cref="Results.Result{TResponse}"/>.
/// Implement <see cref="IQueryHandler{TQuery,TResponse}"/> to handle this request.
/// </summary>
/// <typeparam name="TResponse">The type returned on success.</typeparam>
public interface IQuery<TResponse> { }
```

### Result type (most-used API)

```csharp
/// <summary>
/// Represents the outcome of an operation that may fail.
/// Use implicit conversion from <typeparamref name="TValue"/> or <see cref="Error"/>
/// rather than constructing directly.
/// </summary>
/// <typeparam name="TValue">The value type on success.</typeparam>
/// <example>
/// <code>
/// public async Task&lt;Result&lt;User&gt;&gt; GetUser(int id)
/// {
///     var user = await _repo.FindAsync(id);
///     return user ?? Error.NotFound("User not found");
/// }
/// </code>
/// </example>
public readonly struct Result<TValue>
```

### Error factory methods

```csharp
/// <summary>
/// Creates a not-found error (HTTP 404 equivalent).
/// </summary>
/// <param name="description">Human-readable reason shown to callers.</param>
public static Error NotFound(string description) => ...;

/// <summary>
/// Creates a validation error (HTTP 400 equivalent).
/// Use when FluentValidation rules fail before reaching the handler.
/// </summary>
public static Error Validation(string description, IEnumerable<ValidationFailure> failures) => ...;
```

### DI extension methods

```csharp
/// <summary>
/// Registers the Modulus mediator and all pipeline behaviors in the DI container.
/// Call <see cref="AddPipelineBehavior{TBehavior}"/> after this to add custom behaviors.
/// </summary>
/// <remarks>
/// Pipeline order: UnhandledExceptionBehavior → LoggingBehavior → ValidationBehavior → Handler
/// </remarks>
public static IServiceCollection AddModulusMediator(this IServiceCollection services) => ...;
```

## WARNING: Missing `<example>` Tags Kill Adoption

**The Problem:**

```csharp
// BAD — developer has to guess how to use this
/// <summary>Sends a command through the mediator pipeline.</summary>
Task<Result> Send(ICommand command, CancellationToken ct = default);
```

**Why This Breaks:**
- Developers who never read the README rely solely on IntelliSense while coding
- Without examples, they make mistakes that generate GitHub issues or negative reviews
- NuGet.org renders XML docs with examples in the "Documentation" tab

**The Fix:**

```csharp
/// <summary>Sends a command through the mediator pipeline.</summary>
/// <example>
/// <code>
/// var result = await mediator.Send(new CreateUserCommand("user@example.com"));
/// if (result.IsFailure) return BadRequest(result.Error.Description);
/// </code>
/// </example>
Task<Result> Send(ICommand command, CancellationToken ct = default);
```

## Analyzer Diagnostic Documentation

Each analyzer rule ID (MOD001–MOD005) should have a corresponding XML constant with a `<remarks>`:

```csharp
// In Modulus.Analyzers — each DiagnosticDescriptor
private static readonly DiagnosticDescriptor Rule = new(
    id: "MOD002",
    title: "Handler must return Result",
    messageFormat: "Handler '{0}' returns '{1}' but must return Result or Result<T>",
    category: "ModulusArchitecture",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    description: "All CQRS handlers in a ModulusKit solution must return Result or Result<T> to enable consistent error propagation through the mediator pipeline. Throwing exceptions for expected errors bypasses the Result pattern.");
```

See the **roslyn** skill for full analyzer XML doc patterns.
