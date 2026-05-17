# Distribution Reference

## Contents
- Distribution Channels for ModulusKit
- NuGet.org Presence
- GitHub Repository as Landing Page
- CLI Tool Discovery
- docs/ Site
- Anti-Patterns

---

## Distribution Channels for ModulusKit

ModulusKit distributes via three channels. Each has different discovery mechanics and audience intent:

| Channel | Discovery Mechanic | Audience Intent | Primary CTA |
|---------|-------------------|-----------------|-------------|
| NuGet.org | Search by keyword/tag | Actively evaluating packages | Install |
| GitHub | Search, referral links | Researching / evaluating | Star / Clone |
| `dotnet tool install` | Direct command or doc referral | Already decided to use CLI | Use the tool |

There is no web frontend — `docs/` contains a VitePress site but it is the secondary surface.
NuGet + GitHub README are the primary discovery surfaces.

---

## NuGet.org Presence

Seven packages under `ModulusKit.*`. Each package page on NuGet.org displays:
- `<Description>` (the pitch)
- `<PackageTags>` (search index terms)
- `<Authors>` from `Directory.Build.props`
- `<PackageProjectUrl>` (links to GitHub)
- README rendered from the file referenced by `<PackageReadmeFile>`

### Shared Metadata Location

```xml
<!-- Directory.Build.props — applies to ALL packages -->
<Authors>Adam Wyatt</Authors>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageProjectUrl>https://github.com/adamwyatt34/Modulus</PackageProjectUrl>
<RepositoryUrl>https://github.com/adamwyatt34/Modulus</RepositoryUrl>
```

Update `PackageProjectUrl` here once — it applies everywhere.

### Per-Package README Embedding

```xml
<!-- src/Modulus.Cli/Modulus.Cli.csproj -->
<PackageReadmeFile>README.md</PackageReadmeFile>
<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

The root `README.md` is embedded into the CLI package. Library packages (`Modulus.Mediator`, etc.) each
embed their own `README.md` from their project folder. Keep each package README focused on that package's
API — do not embed the root README into library packages.

---

## GitHub Repository as Landing Page

GitHub's repository root renders `README.md` directly. Developers arriving from a NuGet search will
often click `PackageProjectUrl` and land here. The README must work as a standalone pitch.

Key GitHub-specific concerns:
- The first ~600px of the README renders "above the fold" — the one-line pitch and install command must be there
- Code blocks with `csharp` syntax highlighting render correctly on GitHub
- Badge images in `![badge](url)` format work but add visual noise before the pitch — put them after the install example, not before

### Verifying README Renders

```powershell
# Pack and check the embedded README appears correctly
dotnet pack src/Modulus.Cli/Modulus.Cli.csproj --configuration Release --output ./nupkgs
```

Then upload to a staging NuGet feed or check GitHub preview by pushing to a branch and viewing the repo.

---

## CLI Tool Discovery

The `modulus` tool is installed via:

```powershell
dotnet tool install --global ModulusKit.Cli
```

Discovery path: Developer reads README → sees install command → installs → runs `modulus --help`.
The `--help` output is the first in-tool experience. Make it count.

```powershell
# Verify help output locally
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- add-module --help
```

The `ToolCommandName` in the csproj controls the command name developers type:

```xml
<!-- src/Modulus.Cli/Modulus.Cli.csproj -->
<ToolCommandName>modulus</ToolCommandName>
```

NEVER change `ToolCommandName` between versions without a deprecation notice — it breaks every developer's
existing scripts and aliases.

---

## docs/ Site

The `docs/` directory contains a VitePress-based documentation site. It is a secondary surface — developers
who are evaluating or actively using ModulusKit. Copy here should be more detailed than the README.

The docs site is NOT embedded in NuGet packages. Changes to `docs/` do not affect package discovery.
Focus docs copy on depth: API reference, configuration options, migration guides.

---

## Anti-Patterns

### WARNING: Stale PackageTags After Renaming Features

**The Problem:**
```xml
<!-- Tags reference old name after the feature was renamed -->
<PackageTags>mediatR;mediator;cqrs</PackageTags>
```

**Why This Breaks:**
- NuGet search indexes tags — stale tags pull in wrong searches
- Developers who find the package via a stale tag feel misled

**The Fix:** Audit tags whenever a feature or dependency name changes.

### WARNING: Embedding Root README in Library Packages

**The Problem:**
```xml
<!-- In Modulus.Mediator.csproj — embedding the project-level README -->
<None Include="..\..\README.md" Pack="true" PackagePath="\" />
```

**Why This Breaks:**
- The root README covers 7 packages — irrelevant detail for someone installing only `ModulusKit.Mediator`
- Mediator-specific users see CLI scaffolding content they don't care about

**The Fix:** Each library package should have its own `README.md` scoped to that package's API.
Only the CLI tool embeds the root README (as shown in `Modulus.Cli.csproj`).
