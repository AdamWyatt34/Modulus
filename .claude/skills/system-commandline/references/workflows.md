# System.CommandLine Workflows Reference

## Contents
- Adding a new command end-to-end
- Testing CLI handlers
- Handler execution pattern
- Testing FakeFileSystem scenarios

---

## Adding a New Command End-to-End

Copy this checklist and track progress:

- [ ] Step 1: Create `src/Modulus.Cli/Commands/MyCommand.cs` with static `Create()` factory
- [ ] Step 2: Create `src/Modulus.Cli/Handlers/MyCommandHandler.cs` with `ExecuteAsync` returning `Task<int>`
- [ ] Step 3: Register in `src/Modulus.Cli/Program.cs` via `rootCommand.AddCommand(MyCommand.Create(...))`
- [ ] Step 4: Add handler test class in `tests/Modulus.Cli.Tests/Handlers/MyCommandHandlerTests.cs`
- [ ] Step 5: Run `dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"` — all green

### Step 1 — Command factory

```csharp
// src/Modulus.Cli/Commands/MyCommand.cs
namespace Modulus.Cli.Commands;

public static class MyCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var nameArg = new Argument<string>("name") { Description = "PascalCase name" };

        var moduleOption = new Option<string>("--module") { Description = "Target module", Required = true };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution") { Description = "Path to .slnx (default: auto-find)" };
        solutionOption.Aliases.Add("-s");

        var command = new Command("my-command", "Short description of the command")
        {
            nameArg, moduleOption, solutionOption,
        };

        command.SetAction(async parseResult =>
        {
            var name     = parseResult.GetValue(nameArg)!;
            var module   = parseResult.GetValue(moduleOption)!;
            var solution = parseResult.GetValue(solutionOption);

            if (!CSharpIdentifierValidator.IsValid(name))
            {
                console.WriteError($"'{name}' is not a valid C# identifier. Use PascalCase.");
                return 1;
            }

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new MyCommandHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(name, module, solution);
        });

        return command;
    }
}
```

### Step 2 — Handler

```csharp
// src/Modulus.Cli/Handlers/MyCommandHandler.cs
namespace Modulus.Cli.Handlers;

public sealed class MyCommandHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public async Task<int> ExecuteAsync(string name, string module, string? solutionPath)
    {
        // Resolve solution
        var slnx = solutionFinder.Find(solutionPath);
        if (slnx is null)
        {
            console.WriteError("Could not find a .slnx file. Use --solution to specify one.");
            return 1;
        }

        // Resolve target paths
        var solutionRoot = fileSystem.GetDirectoryName(slnx)!;
        var targetDir = Path.Combine(solutionRoot, "src", module);

        if (!fileSystem.DirectoryExists(targetDir))
        {
            console.WriteError($"Module directory '{targetDir}' does not exist.");
            return 1;
        }

        // Generate and write
        var targetPath = Path.Combine(targetDir, $"{name}.cs");
        if (fileSystem.FileExists(targetPath))
        {
            console.WriteError($"File '{targetPath}' already exists.");
            return 1;
        }

        var content = GenerateContent(name, module);
        fileSystem.WriteAllText(targetPath, content);

        console.WriteSuccess($"Created {name} in {module}");
        return 0;
    }

    private static string GenerateContent(string name, string module) =>
        $$"""
        namespace {{module}};

        public sealed class {{name}}
        {
        }
        """;
}
```

### Step 3 — Register in Program.cs

```csharp
// src/Modulus.Cli/Program.cs — add one line inside the command registration block
rootCommand.AddCommand(MyCommand.Create(fileSystem, console));
```

---

## Testing CLI Handlers

Tests never invoke `Command.SetAction` — they call the handler directly with fake infrastructure.

### Test class structure

```csharp
// tests/Modulus.Cli.Tests/Handlers/MyCommandHandlerTests.cs
namespace Modulus.Cli.Tests.Handlers;

public class MyCommandHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private MyCommandHandler CreateHandler()
    {
        var finder = new SolutionFinder(_fs);
        return new MyCommandHandler(_fs, _console, finder);
    }

    [Fact]
    public async Task ExecuteAsync_ValidInput_CreatesFileAndReturnsZero()
    {
        // Arrange — seed a fake solution and module directory
        _fs.SeedFile(@"C:\work\MyApp\MyApp.slnx", "");
        _fs.SeedDirectory(@"C:\work\MyApp\src\Orders");
        _fs.SetCurrentDirectory(@"C:\work\MyApp");

        var handler = CreateHandler();

        // Act
        var result = await handler.ExecuteAsync("CreateOrder", "Orders", solutionPath: null);

        // Assert
        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\MyApp\src\Orders\CreateOrder.cs").ShouldBeTrue();
        _console.SuccessLines.ShouldContain(x => x.Contains("CreateOrder"));
    }

    [Fact]
    public async Task ExecuteAsync_FileAlreadyExists_ReturnsOne()
    {
        _fs.SeedFile(@"C:\work\MyApp\MyApp.slnx", "");
        _fs.SeedFile(@"C:\work\MyApp\src\Orders\CreateOrder.cs", "// existing");
        _fs.SetCurrentDirectory(@"C:\work\MyApp");

        var result = await CreateHandler().ExecuteAsync("CreateOrder", "Orders", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ModuleNotFound_ReturnsOne()
    {
        _fs.SeedFile(@"C:\work\MyApp\MyApp.slnx", "");
        _fs.SetCurrentDirectory(@"C:\work\MyApp");

        var result = await CreateHandler().ExecuteAsync("CreateOrder", "NonExistentModule", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(x => x.Contains("does not exist"));
    }
}
```

### Asserting process runner invocations

```csharp
// tests/Modulus.Cli.Tests/Handlers/InitHandlerTests.cs (pattern)
private readonly FakeProcessRunner _proc = new();

[Fact]
public async Task ExecuteAsync_WithGit_RunsGitInit()
{
    // Arrange
    _fs.SetCurrentDirectory(@"C:\work");
    var handler = new InitHandler(_fs, _proc, _console);

    // Act
    await handler.ExecuteAsync("MyApp", @"C:\work", false, "inmemory", noGit: false);

    // Assert
    _proc.Invocations.ShouldContain(x => x.Command == "git" && x.Arguments == "init");
}

[Fact]
public async Task ExecuteAsync_NoGit_SkipsGitInit()
{
    _fs.SetCurrentDirectory(@"C:\work");
    var handler = new InitHandler(_fs, _proc, _console);

    await handler.ExecuteAsync("MyApp", @"C:\work", false, "inmemory", noGit: true);

    _proc.Invocations.ShouldNotContain(x => x.Command == "git");
}
```

---

## Handler Execution Pattern

Every handler must follow this order. Skipping steps leads to inconsistent behavior:

```
1. Validate inputs (identifier names, enum values, mutually exclusive flags)
   → return 1 with console.WriteError() on failure

2. Resolve paths (find .slnx, construct target directories)
   → return 1 if solution not found

3. Check preconditions (directory exists, file doesn't already exist)
   → return 1 with descriptive error

4. Generate content (templates, string interpolation)

5. Write files (fileSystem.CreateDirectory + fileSystem.WriteAllText)

6. Run processes if needed (dotnet restore, git init)
   → warn on non-zero but do NOT return 1 — these are advisory

7. Output success (console.WriteSuccess + summary lines)
   → return 0
```

Process failures (step 6) are warnings, not errors. A failed `git init` should not abort an otherwise successful scaffold — the user can run it manually.

---

## FakeFileSystem Seeding Scenarios

### Scenario: command requires finding a .slnx upward from cwd

```csharp
_fs.SeedFile(@"C:\work\MyApp\MyApp.slnx", "");
_fs.SetCurrentDirectory(@"C:\work\MyApp\src\Orders");
// SolutionFinder walks up: src/Orders → src → MyApp → finds MyApp.slnx
```

### Scenario: simulating a non-empty directory (prevents overwrite)

```csharp
_fs.SeedFile(@"C:\work\MyApp\Directory.Build.props", "");
// fileSystem.GetFiles(...) returns non-empty list → handler returns 1
```

### Scenario: fake process runner returns failure

```csharp
_proc.ExitCodeToReturn = 1;
// Handler should warn via console.WriteError but still return 0
var result = await handler.ExecuteAsync(...);
result.ShouldBe(0);
_console.ErrorLines.ShouldContain(x => x.Contains("Warning"));
```

---

## Running CLI Tests

```powershell
# All CLI tests
dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"

# Specific handler
dotnet test Modulus.slnx --filter "FullyQualifiedName~InitHandlerTests"

# Validate and test locally
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- my-command MyName --module Orders
```

Validate: run `dotnet test --filter "FullyQualifiedName~Cli"` — all pass before committing. If a test fails, fix the handler logic, not the test.

See the **xunit** skill for test naming conventions and Shouldly assertion patterns.
