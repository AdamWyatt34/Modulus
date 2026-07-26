# Skill suggestions — Wave B item 2 (multi-target library packages)

Findings from implementing `net8.0;net10.0` multi-targeting that are reusable enough to fold into
`.claude/skills/**` (not applied directly per task instructions — this repo's skills weren't
touched).

## New skill candidate: `dotnet-multitargeting`

A `.claude/skills/dotnet-multitargeting/SKILL.md` would save real re-discovery time; every finding
below came from empirical testing (`dotnet msbuild -getProperty:...`, inspecting
`obj/project.assets.json`), not documentation, because the two relevant behaviors are
under-documented and one is actively surprising:

1. **`TargetFrameworks` (plural) in `Directory.Build.props` + per-project `TargetFramework`
   (singular) override does NOT "just work" for restore**, even though the SDK's own build-time
   cross-targeting split (`Microsoft.NET.Sdk/Sdk/Sdk.targets`) correctly treats a set
   `TargetFramework` as authoritative (`IsCrossTargetingBuild` only goes true when
   `TargetFrameworks != '' AND TargetFramework == ''`). NuGet Restore reads `TargetFrameworks`
   directly whenever it's non-empty, independent of `TargetFramework` — so a project meant to
   single-target restores (and, if its single TFM isn't itself a member of the shared
   `TargetFrameworks` list — e.g. a `netstandard2.0` Roslyn component under a `net8.0;net10.0`
   default — fails outright with NETSDK1005: "Assets file ... doesn't have a target for
   '<tfm>'"). Fix: clear `TargetFrameworks` in `Directory.Build.targets` (which imports *after*
   the csproj body, so the per-project `TargetFramework` is already visible) whenever
   `'$(TargetFramework)' != ''`. One centralized fix covers every single-target project instead of
   repeating an empty-`TargetFrameworks` override in each csproj.
2. **Third-party NuGet packages can have TFM-asset-specific API surface differences** that only
   show up at compile time, per TFM, in code that has compiled fine for years. Shouldly 4.3.0 ships
   separate `net8.0`, `net9.0`, and `netstandard2.0` assemblies; the `net9.0` build (the asset a
   `net10.0` project restores, since NuGet picks the nearest-compatible-and-not-over TFM asset) has
   an `IReadOnlyDictionary<,>` overload of `ShouldContainKey`/`ShouldNotContainKey` that the `net8.0`
   build lacks. A multi-target build is the only thing that surfaces this — worth a generic
   "reflect two TFM assemblies of the same package version and diff their public members" workflow
   snippet (`AssemblyLoadContext` per assembly, since loading two same-identity assemblies into the
   default context throws `FileLoadException`).
3. **`System.Threading.Lock`** (net9.0+) and **`Convert.ToHexStringLower`** (net9.0+) are the two
   net9-or-later BCL additions most likely to sneak into a net10.0-only codebase and then break a
   net8.0 multi-target; grep for both before assuming "no net9/net10-only APIs in `src/`" is still
   true after new code lands. For `Lock` specifically: default to plain `object` monitors unless a
   profiler shows the uncontended-path win actually matters for that specific lock — most
   application-level locks (guarding lazy construction, protecting a small in-memory collection in
   tests) are never contended enough for it to matter, and one code path for every TFM is easier to
   reason about than an `#if NET9_0_OR_GREATER` shim duplicated at every call site.
4. **EF Core is a real, not just nominal, hard fork at major-version boundaries**: `10.0.x` ships no
   `net8.0` assets at all (`8.0.x` is the last EF8 line), which Central Package Management handles
   cleanly via `Condition="'$(TargetFramework)' == '...'"` on `PackageVersion` items — CPM evaluates
   the condition per inner-TFM restore pass, same as any other MSBuild conditional property. Package
   validation (`Microsoft.DotNet.PackageValidation`, the `EnablePackageValidation`/
   `PackageValidationBaselineVersion` machinery) treats *adding* a target framework to an existing
   package as additive, not breaking — no `CompatibilitySuppressions.xml` entries were needed for
   any of the four already-shipped packages purely from the multi-target change itself.

## Existing skill update candidates

- `.claude/skills/ef-core/references/patterns.md`: add a note that `Modulus.Messaging`/
  `Modulus.Testing`'s SQLite-backed tests are the actual regression check for EF8-vs-EF10 drift once
  multi-targeting lands — running the suite on both TFMs (not just compiling on both) is the real
  verification, and any net8.0-only *test failure* (not just a compile error) needs a real
  investigation, not a `[Fact(Skip=...)]`.
- `.claude/skills/csharp/references/patterns.md`: add the `Lock` vs `object` decision from finding 3
  above as a documented DO/DON'T pair — it's exactly the kind of "obvious latest-C#-feature choice
  that quietly breaks portability" this project's conventions doc otherwise pins down for other
  APIs (nullable, primary constructors, etc.).
