# Measurement & Testing Reference

## Contents
- What to Measure for a Developer Library
- Proxies for Conversion (No Analytics Dashboard)
- Testing Copy Quality
- Validating CLI Help Text
- Feedback Loop for Messaging Changes

---

## What to Measure for a Developer Library

ModulusKit has no frontend analytics. "Conversion" must be inferred from proxy signals:

| Signal | What It Indicates | Where to Check |
|--------|------------------|----------------|
| NuGet download count | Package discovery → install conversion | NuGet.org package stats |
| GitHub stars | Repository reach and interest | GitHub Insights |
| GitHub issues | Friction in getting started (bad copy = confused users) | Issues tab |
| CLI usage errors | Poor help text → wrong input → error messages hit | `WriteError` call frequency |
| `dotnet tool install` failures | Distribution or naming issues | NuGet package version history |

None of these are instrumented in code — they are external signals. The only in-code signal is the
error path: if `WriteError` is hit frequently with the same message, that message is not preventing
the error. Either the help text needs to be clearer, or the validation needs to happen earlier.

---

## Proxies for Conversion (No Analytics Dashboard)

### GitHub Issues as Copy Feedback

When a GitHub issue contains phrases like:
- "I couldn't figure out how to..."
- "The README doesn't explain..."
- "What does `--transport` mean?"

...that is direct evidence of a copy failure. The fix belongs in the description, README, or error message,
not in a new issue response.

### NuGet Downloads as A/B Signal

NuGet does not support A/B testing descriptions. The only way to test copy is to change it and observe
the download trend over the next release cycle. Tag messaging changes in commit messages so you can
correlate them with download data:

```
feat: update ModulusKit.Generators description to highlight "zero reflection" value prop
```

---

## Testing Copy Quality

### Test 1: The 5-Second Description Test

Read the `<Description>` for each package. After 5 seconds, answer:
1. What does this package do?
2. Is it relevant to my current project?
3. What's the key differentiator vs. alternatives?

If you can't answer all three, the description needs work.

### Test 2: The --help Self-Sufficiency Test

```powershell
# Run --help for each command WITHOUT reading any other docs
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-module --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-entity --help
```

For each option, ask: "Could a developer who has never seen this codebase understand what this option does
and what value to pass?" If not, the description is incomplete.

### Test 3: Error Message Recovery Test

```powershell
# Deliberately trigger each validation error
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init "invalid name!"
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init MySolution --transport badvalue
```

After reading the error output, ask: "Could I fix this without reading any docs?" Every error should
be self-correcting.

---

## Validating CLI Help Text

Run this after any change to `Commands/*.cs` option/argument descriptions:

```powershell
# Build and test help output for all commands
dotnet build src/Modulus.Cli/Modulus.Cli.csproj
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-module --help
```

Checklist for each command's `--help` output:

- [ ] Command description answers: "What does this command produce?"
- [ ] All required arguments describe their expected format
- [ ] All options with enumerated values list valid values in the description
- [ ] Default values are mentioned for optional options
- [ ] No option description is blank

---

## Feedback Loop for Messaging Changes

1. Identify the surface to change (description, option text, console message)
2. Read the current text: `Read src/Modulus.Cli/Commands/InitCommand.cs`
3. Draft the new text using formulas from [content-copy](content-copy.md)
4. Apply the change with `Edit`
5. Validate: `dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- [command] --help`
6. If the output looks wrong or truncated, fix and repeat step 5
7. For `.csproj` changes: `dotnet pack src/[Project]/[Project].csproj --output ./nupkgs` and inspect the .nupkg metadata

The only automated test for copy correctness is reading the rendered output. There are no xUnit tests
for description strings — rely on the manual validation loop above.
