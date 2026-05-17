# Measurement & Testing Reference

## Contents
- Adoption Funnel Signals
- Package Health Metrics
- CLI Telemetry Considerations
- Testing the Offer Ladder Itself
- Anti-Patterns in Library Measurement

For an open-source library ecosystem, "measurement" means tracking NuGet download signals, GitHub engagement, and ensuring the adoption ladder is structurally sound through tests. There is no runtime user funnel — the funnel is the developer experience.

---

## Adoption Funnel Signals

The adoption funnel for ModulusKit:

```
NuGet search discovery
  → Package page view (README quality matters here)
    → dotnet add package (Tier 1 or 2 install)
      → First successful build (Tier 2 adoption confirmed)
        → AddModulusHandlers() usage (Tier 3 adoption confirmed)
          → modulus init usage (Tier 4 / CLI adoption confirmed)
```

Observable signals (NuGet.org stats):
- **Downloads per package** — `ModulusKit.Mediator.Abstractions` downloads should be ≤ `ModulusKit.Mediator` downloads. If abstractions exceeds runtime, developers are installing without the implementation.
- **Version adoption curve** — % of downloads on latest version. Slow adoption of new versions indicates breaking changes or unclear migration docs.
- **CLI vs. library ratio** — `ModulusKit.Cli` downloads (tool installs) vs. `ModulusKit.Mediator` downloads. Low CLI ratio means the scaffold experience isn't being discovered.

---

## Package Health Metrics

Track these signals to detect offer-ladder problems:

| Signal | Healthy | Warning |
|--------|---------|---------|
| Abstractions/Runtime download ratio | < 0.3 | > 1.0 (installs without runtime) |
| GitHub issues tagged `question` | Low | High (docs/README gap) |
| `modulus init` issues | Near zero | Any (CLI UX problem) |
| Version adoption rate (30d) | > 60% on latest | < 30% |

---

## Testing the CLI Offer Ladder

The CLI is the Tier 4 entry point. Its handler tests in `Modulus.Cli.Tests` validate the scaffolded output is structurally correct — a broken scaffold is a broken offer ladder.

```csharp
// Modulus.Cli.Tests — validates scaffold output matches expected structure
[Fact]
public async Task InitHandler_WithAspire_ScaffoldsAspireProject()
{
    // Arrange
    var fs = new FakeFileSystem();
    var console = new FakeConsole();
    var runner = new FakeProcessRunner();
    var handler = new InitHandler(fs, runner, console);

    // Act
    var exitCode = await handler.ExecuteAsync("MyApp", aspire: true, transport: "rabbitmq");

    // Assert
    exitCode.ShouldBe(0);
    fs.FileExists("MyApp/src/MyApp.AppHost/MyApp.AppHost.csproj").ShouldBeTrue();
    fs.FileExists("MyApp/src/MyApp.Api/Program.cs").ShouldBeTrue();
}
```

```csharp
// Validate that scaffolded Program.cs includes all tier wiring
[Fact]
public async Task InitHandler_ScaffoldedProgramCs_ContainsMediatorAndMessagingRegistrations()
{
    var fs = new FakeFileSystem();
    var handler = new InitHandler(fs, new FakeProcessRunner(), new FakeConsole());
    await handler.ExecuteAsync("MyApp", aspire: false, transport: "inmemory");

    var programCs = fs.ReadFile("MyApp/src/MyApp.Api/Program.cs");
    programCs.ShouldContain("AddModulusMediator");
    programCs.ShouldContain("AddModulusMessaging");
    programCs.ShouldContain("AddModulusHandlers");
}
```

---

## Testing Package Adoption Path

Generator tests validate that Tier 3 (source generator) produces correct output for a Tier 2 (runtime) consumer:

```csharp
// Modulus.Generators.Tests — validates AddModulusHandlers() is generated correctly
[Fact]
public Task Generator_WithCommandAndQueryHandler_EmitsCorrectRegistrations()
{
    var source = """
        using Modulus.Mediator.Abstractions.Messaging;
        using Modulus.Mediator.Abstractions.Results;

        public record GetOrderQuery(int Id) : IQuery<Order>;
        public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, Order>
        {
            public Task<Result<Order>> Handle(GetOrderQuery q, CancellationToken ct) => throw new NotImplementedException();
        }
        """;

    return TestHelper.Verify(source); // snapshot tests the generated AddModulusHandlers() output
}
```

---

## WARNING: No Telemetry in CLI Without Explicit Opt-In

**The Problem:**
```csharp
// BAD — collecting telemetry without consent
public async Task<int> ExecuteAsync(string solutionName)
{
    await _analytics.TrackAsync("init_command", new { solutionName }); // ❌
    // ...
}
```

**Why This Breaks:**
1. Developers distrust tools that phone home without explicit consent
2. Enterprise environments block outbound connections, causing CLI hangs
3. Opens-source library trust depends on transparency

**The Fix:**
If telemetry is ever added, gate it explicitly:
```csharp
// GOOD — opt-in only, checked before any network call
if (_config.TelemetryEnabled)
{
    await _analytics.TrackAsync("init_command");
}
```

---

## Iterate-Until-Pass: Offer Ladder Validation

Run after any scaffolding or generator change:

1. Build and test: `dotnet test Modulus.slnx`
2. If generator tests fail: fix generator output, rebuild, repeat step 1
3. Validate CLI scaffold: `dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init TestSolution`
4. If scaffold is malformed: fix template in `src/Modulus.Templates/`, rebuild, repeat step 3
5. Pack and inspect: `dotnet pack Modulus.slnx --output ./nupkgs`
6. Only proceed to tag release when all steps pass clean

See the **xunit** skill for test project structure and the **system-commandline** skill for CLI handler testing patterns.
