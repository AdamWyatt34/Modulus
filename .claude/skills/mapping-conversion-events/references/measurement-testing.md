# Measurement and Testing Reference

## Contents
- What to Measure for a Developer Library
- Funnel Metrics by Stage
- Testing CLI Conversion Flows
- Exit Code Assertions
- Anti-Patterns in Measurement

---

## What to Measure for a Developer Library

ModulusKit has no user sessions, no auth, and no server-side telemetry. Measurement happens through:

1. **NuGet.org stats** — download counts per package per version (external, read-only)
2. **GitHub Insights** — traffic, clones, referrers (external, read-only)
3. **CLI exit codes** — the only in-process activation signal you control
4. **Test coverage of handler paths** — validates that success/failure paths produce correct exit codes

NEVER instrument the CLI with telemetry without explicit opt-in — developer tools that phone home without consent are immediately uninstalled and cause reputational damage.

---

## Funnel Metrics by Stage

| Stage | Metric | Source | Benchmark Signal |
|-------|--------|--------|-----------------|
| Discovery | Unique package page views | NuGet.org stats | Trending = 100+/week |
| Trial | Total downloads | NuGet.org stats | Install-to-active ratio |
| Activation | `modulus init` exit 0 | CLI tests (proxy) | 0 known blocking errors |
| Adoption | `add-module` invocation count | CLI tests (proxy) | Handler tests pass |
| Retention | Download growth rate | NuGet.org stats | Week-over-week delta |

The only metrics you can actively improve through code changes are the ones tied to CLI exit codes and error copy.

---

## Testing CLI Conversion Flows

Handler tests in `tests/Modulus.Cli.Tests/` are the closest proxy for measuring activation success. A failing handler test means a broken activation path.

```csharp
// tests/Modulus.Cli.Tests/Handlers/InitHandlerTests.cs
// Test the activation path: valid input → exit code 0
[Fact]
public async Task ExecuteAsync_ValidSolutionName_ReturnsZero()
{
    // Arrange
    var fs = new FakeFileSystem();
    var runner = new FakeProcessRunner();
    var console = new FakeConsole();
    var handler = new InitHandler(fs, runner, console);

    // Act
    var exitCode = await handler.ExecuteAsync("EShop", aspire: false, transport: "inmemory");

    // Assert
    exitCode.ShouldBe(0); // activation signal
}
```

```csharp
// Test the drop-off path: invalid input → exit code 1 with actionable error
[Fact]
public async Task ExecuteAsync_InvalidSolutionName_ReturnsOne_WithHelpfulError()
{
    // Arrange
    var console = new FakeConsole();
    var handler = new InitHandler(new FakeFileSystem(), new FakeProcessRunner(), console);

    // Act
    var exitCode = await handler.ExecuteAsync("my-app", aspire: false, transport: "inmemory");

    // Assert
    exitCode.ShouldBe(1); // drop-off signal
    console.ErrorOutput.ShouldContain("valid C# identifier"); // error must be actionable
}
```

---

## Exit Code Assertions

Every handler test should assert BOTH the exit code AND the console output. Exit code alone doesn't confirm the user was unblocked:

```csharp
// Validate the add-module activation path
[Fact]
public async Task ExecuteAsync_ValidModule_WritesSuccessMessage()
{
    // Arrange
    var fs = new FakeFileSystem();
    fs.SeedDirectory("EShop"); // existing solution
    var console = new FakeConsole();
    var handler = new AddModuleHandler(fs, new FakeProcessRunner(), console);

    // Act
    var exitCode = await handler.ExecuteAsync("Catalog");

    // Assert
    exitCode.ShouldBe(0);
    console.Output.ShouldContain("Catalog"); // confirm module name echoed in success message
    console.Output.ShouldContain("modulus add-module"); // confirm next step surfaced
}
```

---

## Validate → Build Feedback Loop

After any change to `InitHandler` or `AddModuleHandler`, run this loop before pushing:

```
1. Make changes to handler or console output
2. Run: dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"
3. If tests fail, fix the handler — do NOT adjust tests to pass
4. Check FakeConsole.Output for activation-moment copy correctness
5. Repeat until all handler tests pass with exit code 0 for valid inputs
```

---

## Anti-Patterns in Measurement

### WARNING: Using Download Count as Activation Proxy

**The Problem:** NuGet download counts include CI restores, automated bots, version bumps triggering re-installs, and developers who installed but never ran a command.

**Why This Breaks:** Optimizing for downloads produces different decisions than optimizing for activation (exit code 0 from `modulus init`). You'll invest in discoverability when the real problem is error UX.

**The Fix:** Track download-to-activation ratio. If downloads climb but issues about first-run failures also climb, the problem is activation, not discovery.

### WARNING: Asserting Only Exit Code in Tests

**The Problem:**

```csharp
// BAD — exit code 0 tells you it "worked" but not that the user was guided correctly
exitCode.ShouldBe(0);
```

**Why This Breaks:** A handler can return 0 while printing nothing, leaving the developer with a created directory and no idea what to do next. Silent success is a conversion killer.

**The Fix:** Assert on `FakeConsole.Output` for the minimum viable next-step message alongside exit code:

```csharp
exitCode.ShouldBe(0);
console.Output.ShouldContain("modulus add-module"); // next step must be surfaced
```

### WARNING: No Tests for Invalid Input Paths

If `InitHandler` has no tests for invalid `solutionName` values, you have zero visibility into drop-off copy. Every invalid input test is also a test of the error message a developer will see before abandoning.
