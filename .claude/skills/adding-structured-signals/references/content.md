# Content Reference — Structured Signals

## Contents
- README Content Hierarchy
- XML Doc Content Patterns
- CLI Help Text
- Release Notes / Changelog
- Anti-Patterns

---

## README Content Hierarchy

A developer deciding to adopt a library follows this scanning pattern in under 10 seconds:
1. Badge line (build status, NuGet version, license)
2. One-line pitch
3. Install command
4. 10-line working example
5. Comparison to alternatives (why this, not MediatR?)

```markdown
# ModulusKit

[![Build](https://img.shields.io/github/actions/workflow/status/AdamWyatt34/Modulus/ci.yml)](https://github.com/AdamWyatt34/Modulus/actions)
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Mediator)](https://www.nuget.org/packages/ModulusKit.Mediator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Scaffold production-ready .NET 10 modular monoliths in minutes. Custom CQRS mediator,
transactional outbox/inbox, and Roslyn source generators — no MediatR, no boilerplate.

## Install

\`\`\`
dotnet tool install -g ModulusKit.Cli
modulus init MyApp --aspire
\`\`\`

## Why ModulusKit?

| Feature | ModulusKit | MediatR + manual wiring |
|---------|-----------|------------------------|
| Handler registration | Auto-generated at compile time | Manual DI registration |
| Result pattern | Built-in, implicit conversions | Third-party or custom |
| Transactional outbox | Included | DIY or Wolverine |
| Architecture enforcement | Roslyn analyzers (MOD001-MOD005) | None |
| Scaffolding CLI | `modulus init` | None |
```

## XML Doc Content Patterns

### Result Type Documentation

```csharp
/// <summary>
/// Represents the outcome of an operation that may succeed or fail without a return value.
/// </summary>
/// <remarks>
/// Use implicit conversion from <see cref="Error"/> for concise failure returns:
/// <code>
/// return Error.NotFound("User not found");
/// </code>
/// Use <see cref="Success"/> for explicit success:
/// <code>
/// return Result.Success();
/// </code>
/// </remarks>
public readonly struct Result
{
    /// <summary>Gets a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Gets whether this result represents a successful operation.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the error, or <see langword="null"/> if the operation succeeded.</summary>
    public Error? Error { get; }
}
```

### Error Type Documentation

```csharp
/// <summary>Classifies why an operation failed.</summary>
/// <remarks>
/// Maps to HTTP status codes when used in API contexts:
/// <list type="table">
/// <listheader><term>ErrorType</term><description>HTTP Status</description></listheader>
/// <item><term>Validation</term><description>400 Bad Request</description></item>
/// <item><term>NotFound</term><description>404 Not Found</description></item>
/// <item><term>Conflict</term><description>409 Conflict</description></item>
/// <item><term>Unauthorized</term><description>401 Unauthorized</description></item>
/// <item><term>Forbidden</term><description>403 Forbidden</description></item>
/// <item><term>Failure</term><description>500 Internal Server Error</description></item>
/// </list>
/// </remarks>
public enum ErrorType { Validation, NotFound, Conflict, Unauthorized, Forbidden, Failure }
```

## CLI Help Text

System.CommandLine renders `description` parameters in `--help` output. This is the UX copy for
the CLI tool. See the **system-commandline** skill for how to wire these.

```csharp
// src/Modulus.Cli/Commands/InitCommand.cs
var command = new Command(
    "init",
    "Scaffold a new modular monolith solution using ModulusKit conventions.");

command.AddArgument(new Argument<string>(
    "solution-name",
    "PascalCase name for the solution and root namespace (e.g., MyApp)."));

command.AddOption(new Option<bool>(
    "--aspire",
    "Include .NET Aspire AppHost and ServiceDefaults projects for local orchestration."));

command.AddOption(new Option<string>(
    "--transport",
    () => "inmemory",
    "Message transport for integration events: inmemory | rabbitmq | azureservicebus."));
```

## Release Notes / Changelog

Keep a `CHANGELOG.md` in the root. NuGet.org does not render it, but GitHub releases link to it.
Use `<PackageReleaseNotes>` for short NuGet-embedded notes:

```xml
<PackageReleaseNotes>
  v1.2.0: Added MetricsBehavior to the default pipeline. Added MOD005 analyzer for public setters.
  v1.1.0: Source generator now discovers AbstractValidator implementations automatically.
  v1.0.0: Initial release of all 7 ModulusKit packages.
</PackageReleaseNotes>
```

---

## WARNING: No Working Example in README

The most common reason developers don't adopt a library: they can't figure out how to use it in
under 2 minutes. Every README must have a 10-15 line working example that compiles and runs.

```markdown
<!-- BAD — description without runnable example -->
ModulusKit provides a mediator that routes commands and queries through a pipeline.

<!-- GOOD — copy-pasteable example -->
## Quick Start

\`\`\`csharp
// 1. Register
builder.Services.AddModulusMediator();

// 2. Define
public record GetUserQuery(int Id) : IQuery<User>;

// 3. Handle
public sealed class GetUserHandler(IUserRepository repo) : IQueryHandler<GetUserQuery, User>
{
    public async Task<Result<User>> Handle(GetUserQuery query, CancellationToken ct)
        => await repo.FindAsync(query.Id) ?? Error.NotFound($"User {query.Id} not found");
}

// 4. Send
var result = await mediator.Send(new GetUserQuery(42));
\`\`\`
```

## WARNING: Stale XML Docs After Refactoring

When renaming public API members, XML doc `<see cref="..."/>` references break silently unless
`GenerateDocumentationFile` is set — the compiler emits CS1574 warnings for broken cref targets.
Build with `/warnaserror:CS1574` in CI to catch these.
