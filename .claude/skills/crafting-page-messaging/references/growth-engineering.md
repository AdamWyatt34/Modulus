# Growth Engineering Reference

## Contents
- Developer Library Growth Levers
- CLI as a Growth Surface
- Version Messaging
- Scaffolded Output as Embedded Marketing
- New Command Messaging Checklist

---

## Developer Library Growth Levers

Growth for ModulusKit means expanding from "installed by one dev" to "adopted by a team" to "referenced
in public projects." The growth levers available in-code:

| Lever | Mechanism | Where to Implement |
|-------|-----------|-------------------|
| **Scaffolded README** | Generated projects include a `README.md` referencing ModulusKit | `Modulus.Templates` |
| **CLI success messaging** | Post-init output points developers to next steps | `InitHandler.cs` |
| **Analyzer messages** | MOD001-MOD005 diagnostics appear in IDE — reinforce the pattern | `Modulus.Analyzers` |
| **Error recovery copy** | Clear error messages reduce abandonment at first friction | `Handlers/*.cs` |
| **Git commit message** | The auto-generated initial commit tags the project as ModulusKit-scaffolded | `InitHandler.cs` |

---

## CLI as a Growth Surface

The CLI is the highest-leverage copy surface for growth. A developer who runs `modulus init` successfully
will likely run `modulus add-module` next. Every successful command output is an opportunity to surface
the next action.

### Current Next-Step Output Pattern

```csharp
// src/Modulus.Cli/Handlers/InitHandler.cs
console.WriteSuccess($"Solution '{solutionName}' created successfully at {solutionRoot}");
console.WriteLine($"  Aspire: {(includeAspire ? "Yes" : "No")}");
console.WriteLine($"  Transport: {transport}");
console.WriteLine($"  Git: {(noGit ? "Skipped" : "Initialized")}");
```

This confirms what was done. To drive next-step adoption, add what to do next:

```csharp
// PROPOSED — add after the summary
console.WriteLine("");
console.WriteLine("Next steps:");
console.WriteLine($"  cd {solutionName}");
console.WriteLine($"  modulus add-module <ModuleName>");
console.WriteLine($"  dotnet run --project src/{solutionName}.Api");
```

See the **system-commandline** skill for adding new output to handlers.

---

## Version Messaging

`Version` is defined in `Directory.Build.props` and applies to all 7 packages simultaneously:

```xml
<!-- Directory.Build.props -->
<Version>1.0.1</Version>
```

When cutting a new version, the release notes and changelog are the only external copy surface for
communicating what changed. Write them as developer-facing value statements, not commit dumps:

```
# BAD — commit dump
v1.0.1: fix null ref, update deps, add VersionCommand

# GOOD — value statement
v1.0.1: VersionCommand (`modulus version`) for CI pipelines, null safety fixes in AddEntityHandler
```

The `VersionCommand` in `src/Modulus.Cli/Commands/VersionCommand.cs` outputs the version to stdout —
ensure the description reflects its CI/scripting use case, not just "shows version."

---

## Scaffolded Output as Embedded Marketing

Every solution scaffolded by `modulus init` generates files via `Modulus.Templates`. The generated
`README.md` template and commit message are growth surfaces:

```csharp
// src/Modulus.Cli/Handlers/InitHandler.cs
await processRunner.RunAsync("git", "commit -m \"Initial commit from Modulus\"", solutionRoot);
```

The git commit message `"Initial commit from Modulus"` appears in every scaffolded project's git history.
This is subtle brand presence in public repositories. Keep it attributive without being spammy.

If the `Modulus.Templates` project generates a `README.md`, that README should include:
- "Scaffolded with [ModulusKit CLI](https://github.com/adamwyatt34/Modulus)"
- The `modulus add-module` command for growing the solution

---

## New Command Messaging Checklist

When adding a new CLI command, use this checklist to ensure messaging is complete:

Copy this checklist and track progress:
- [ ] Step 1: Write command description — verb + object + context (e.g., "Add a new entity to an existing module")
- [ ] Step 2: Write all argument descriptions — include format constraint (e.g., "PascalCase name of the entity")
- [ ] Step 3: Write all option descriptions — outcome sentence or enumerated values with parens
- [ ] Step 4: Write the success `WriteSuccess` message — confirm artifact name and location
- [ ] Step 5: Write the summary `WriteLine` block — echo back all applied options/defaults
- [ ] Step 6: Write all `WriteError` messages — problem + fix in one line, echo the bad value
- [ ] Step 7: Run `dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- [command] --help` and verify
- [ ] Step 8: Trigger each error path and verify recovery is possible from error message alone
