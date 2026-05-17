# System.CommandLine Patterns Reference

## Contents
- Option types and when to use each
- Argument vs Option
- Infrastructure abstractions
- WARNING: Anti-patterns

---

## Option Types

### `Option<string>` — required string with default

```csharp
var transportOption = new Option<string>("--transport")
{
    Description = "Messaging transport (inmemory, rabbitmq, azureservicebus)",
    DefaultValueFactory = _ => "inmemory",
};
```

Use when the option always has a sensible default. `parseResult.GetValue()` never returns null.

### `Option<string?>` — truly optional string

```csharp
var outputOption = new Option<string?>("--output")
{
    Description = "Output directory (default: current directory)",
};
outputOption.Aliases.Add("-o");
```

Returns `null` when not provided. Handle with `?? fallback`:

```csharp
var output = parseResult.GetValue(outputOption) ?? fileSystem.GetCurrentDirectory();
```

### `Option<bool>` — flag (presence = true)

```csharp
var aspireOption = new Option<bool>("--aspire")
{
    Description = "Include .NET Aspire projects",
};
```

No value required on the command line — `--aspire` sets it `true`.

### Required option

```csharp
var moduleOption = new Option<string>("--module") { Required = true };
moduleOption.Aliases.Add("-m");
```

System.CommandLine enforces presence automatically; the error message is shown before `SetAction` runs.

---

## Argument vs Option

| Use `Argument<T>` | Use `Option<T>` |
|-------------------|-----------------|
| Positional, always required | Named (`--flag`), optional or required |
| First thing user types | User chooses order |
| `new Argument<string>("solution-name")` | `new Option<string>("--module")` |

```csharp
// Argument — positional, no flag needed
var nameArg = new Argument<string>("solution-name")
{
    Description = "PascalCase solution name",
};

// Option — named flag
var transportOption = new Option<string>("--transport") { ... };
```

---

## Infrastructure Abstractions

All three abstractions live in `src/Modulus.Cli/Infrastructure/`. Use them — never call `File.WriteAllText`, `Console.WriteLine`, or `Process.Start` directly in handlers.

### `IFileSystem`

```csharp
// Writing a file
fileSystem.CreateDirectory(dir);
fileSystem.WriteAllText(fullPath, content);

// Checking existence before overwrite
if (fileSystem.DirectoryExists(solutionRoot))
{
    var files = fileSystem.GetFiles(solutionRoot, "*", SearchOption.TopDirectoryOnly);
    if (files.Count > 0)
    {
        console.WriteError($"Directory '{solutionRoot}' is not empty.");
        return 1;
    }
}
```

### `IConsoleOutput`

```csharp
console.WriteLine("Normal output");
console.WriteError("Something went wrong — printed to stderr");
console.WriteSuccess("Done! Solution created at ...");
```

### `IProcessRunner`

```csharp
var exitCode = await processRunner.RunAsync("dotnet", "restore", workingDir);
if (exitCode != 0)
    console.WriteError("Warning: dotnet restore failed. Run it manually.");
```

---

## WARNING: Anti-Patterns

### WARNING: Putting business logic in SetAction

**The Problem:**

```csharp
// BAD — business logic mixed into command factory
command.SetAction(async parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    var files = templateEngine.Generate(name);
    foreach (var f in files)
        File.WriteAllText(f.Path, f.Content);  // also calls File directly
    return 0;
});
```

**Why This Breaks:**
1. Untestable — you can't invoke `SetAction` directly in tests
2. Bypasses `IFileSystem` abstraction — breaks `FakeFileSystem` test doubles
3. The command factory becomes a god method

**The Fix:**

```csharp
// GOOD — SetAction only wires arguments and delegates
command.SetAction(async parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    var handler = new MyHandler(fileSystem, console);
    return await handler.ExecuteAsync(name);
});
```

---

### WARNING: Calling `File.*` or `Console.*` directly in handlers

**The Problem:**

```csharp
// BAD — bypasses abstractions
public async Task<int> ExecuteAsync(string name)
{
    File.WriteAllText(path, content);
    Console.WriteLine("Done.");
    return 0;
}
```

**Why This Breaks:**
1. Tests cannot intercept writes — `FakeFileSystem` is never used
2. Tests produce real console output — noisy, not assertable
3. Cross-platform path handling differs from `IFileSystem.GetDirectoryName()`

**The Fix:**

```csharp
// GOOD
public async Task<int> ExecuteAsync(string name)
{
    fileSystem.WriteAllText(path, content);
    console.WriteSuccess("Done.");
    return 0;
}
```

---

### WARNING: Skipping input validation before handler instantiation

**The Problem:**

```csharp
// BAD — invalid name reaches the handler
command.SetAction(async parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    var handler = new MyHandler(fileSystem, console);
    return await handler.ExecuteAsync(name);  // handler must now validate
});
```

**Why This Breaks:**
1. Validation logic leaks into the handler — harder to unit-test in isolation
2. Handler must now decide whether to return 1 or throw — ambiguous

**The Fix:**

```csharp
// GOOD — validate in SetAction, handler receives clean input
command.SetAction(async parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    if (!CSharpIdentifierValidator.IsValid(name))
    {
        console.WriteError($"'{name}' is not a valid C# identifier.");
        return 1;
    }
    var handler = new MyHandler(fileSystem, console);
    return await handler.ExecuteAsync(name);
});
```

---

### WARNING: Non-sealed handler classes

**The Problem:**

```csharp
// BAD
public class InitHandler(IFileSystem fs, IConsoleOutput console) { ... }
```

**Why:** All DI-injected classes in this codebase are `sealed`. Non-sealed classes invite accidental subclassing and signal that inheritance is intended — it never is here.

**The Fix:** Always `sealed`:

```csharp
public sealed class InitHandler(IFileSystem fs, IConsoleOutput console) { ... }
```

---

## `SolutionFinder` Pattern

Commands that operate on an existing solution use `SolutionFinder` to auto-locate the `.slnx` file:

```csharp
command.SetAction(async parseResult =>
{
    var solution = parseResult.GetValue(solutionOption);  // optional override

    var solutionFinder = new SolutionFinder(fileSystem);
    var handler = new AddModuleHandler(fileSystem, console, solutionFinder);
    return await handler.ExecuteAsync(moduleName, solution);
});
```

`SolutionFinder` walks up from `GetCurrentDirectory()` — tests seed it via `FakeFileSystem.SeedFile()`.
