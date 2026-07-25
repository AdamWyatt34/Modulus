using System.Text.RegularExpressions;
using System.Xml.Linq;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

/// <summary>
/// Bumps every ModulusKit.* pin in the solution's Directory.Packages.props to a target
/// version. The file is parsed with XDocument only to identify which entries exist; the
/// edit itself is a line-scoped string replacement so formatting and comments survive.
/// </summary>
public sealed class UpgradeHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    /// <summary>
    /// A pragmatic NuGet version pattern: 2-4 numeric segments, an optional dot-separated
    /// alphanumeric prerelease label, and an optional dot-separated alphanumeric build-metadata
    /// label (NuGet allows 2-4 numeric segments, unlike strict three-segment SemVer). This is
    /// deliberately restrictive rather than exhaustive: it exists to keep a hand-typed
    /// <c>--version</c> value safe to splice into an XML attribute and a regex replacement
    /// string, not to validate every technically-legal NuGet version string.
    /// </summary>
    private static readonly Regex NuGetVersionPattern = new(
        @"^\d+(\.\d+){1,3}(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?(\+[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$",
        RegexOptions.Compiled);

    public Task<int> ExecuteAsync(string? version, string? solutionPath, bool dryRun)
    {
        // Same default as `init`: the CLI's own MinVer-stamped version.
        var targetVersion = string.IsNullOrWhiteSpace(version)
            ? InitHandler.ResolveCliVersion()
            : version;

        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            console.WriteError("Could not determine a target version. Pass one explicitly with --version.");
            return Task.FromResult(1);
        }

        if (!NuGetVersionPattern.IsMatch(targetVersion))
        {
            console.WriteError($"'{targetVersion}' is not a valid NuGet package version (expected e.g. '1.2.3' or '1.2.3-preview.1').");
            return Task.FromResult(1);
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError(solutionFinder.DescribeResolutionFailure(solutionPath));
            return Task.FromResult(1);
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))
            ?? throw new InvalidOperationException($"Could not determine directory for path: {slnxPath}");

        var propsPath = Path.Combine(solutionRoot, "Directory.Packages.props");
        if (!fileSystem.FileExists(propsPath))
        {
            console.WriteError($"'Directory.Packages.props' was not found at '{propsPath}'.");
            return Task.FromResult(1);
        }

        var content = fileSystem.ReadAllText(propsPath);

        List<(string Include, string? Version)> pins;
        try
        {
            pins = XDocument.Parse(content)
                .Descendants()
                .Where(e => e.Name.LocalName == "PackageVersion")
                .Select(e => (Include: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version")))
                .Where(p => p.Include is not null && p.Include.StartsWith("ModulusKit.", StringComparison.Ordinal))
                .Select(p => (p.Include!, p.Version))
                .ToList();
        }
        catch (Exception ex)
        {
            console.WriteError($"'{propsPath}' is not well-formed XML: {ex.Message}");
            return Task.FromResult(1);
        }

        if (pins.Count == 0)
        {
            console.WriteLine("No ModulusKit.* package pins found in Directory.Packages.props. Nothing to upgrade.");
            return Task.FromResult(0);
        }

        var updated = content;
        var changed = 0;
        var failures = new List<string>();

        console.WriteLine($"Target version: {targetVersion}");
        console.WriteLine("");
        console.WriteLine($"{"Package",-42} {"From",-16} To");

        foreach (var (include, currentVersion) in pins)
        {
            if (string.Equals(currentVersion, targetVersion, StringComparison.Ordinal))
            {
                console.WriteLine($"{include,-42} {currentVersion,-16} (already at target)");
                continue;
            }

            // Anchored to this package's own element so an identical version literal on a
            // non-ModulusKit line is never touched.
            var pattern = $"""(<PackageVersion\s+Include="{Regex.Escape(include)}"\s+Version=")[^"]*(")""";

            // A MatchEvaluator delegate is used (not a "$1{targetVersion}$2" replacement string)
            // so targetVersion is spliced in verbatim: Regex.Replace's string-replacement overload
            // treats "$" specially ($1, $$, $&, ...), and while NuGetVersionPattern above already
            // rejects "$" in practice, this removes the failure mode entirely rather than relying
            // on validation elsewhere never being loosened.
            var replaced = Regex.Replace(updated, pattern, m => $"{m.Groups[1].Value}{targetVersion}{m.Groups[2].Value}");

            if (ReferenceEquals(replaced, updated) || replaced == updated)
            {
                failures.Add(include);
                console.WriteLine($"{include,-42} {currentVersion,-16} SKIPPED (unrecognized formatting)");
                continue;
            }

            updated = replaced;
            changed++;
            console.WriteLine($"{include,-42} {currentVersion,-16} {targetVersion}");
        }

        console.WriteLine("");

        if (failures.Count > 0)
        {
            console.WriteError(
                $"Could not rewrite {failures.Count} entr{(failures.Count == 1 ? "y" : "ies")} " +
                $"({string.Join(", ", failures)}). Update them manually.");
        }

        if (changed == 0)
        {
            console.WriteLine(failures.Count == 0
                ? "All ModulusKit.* pins are already at the target version."
                : "No changes written.");
            return Task.FromResult(failures.Count > 0 ? 1 : 0);
        }

        if (dryRun)
        {
            console.WriteLine($"Dry run: {changed} pin(s) would be updated. Re-run without --dry-run to apply.");
            return Task.FromResult(failures.Count > 0 ? 1 : 0);
        }

        fileSystem.WriteAllText(propsPath, updated);
        console.WriteSuccess($"Updated {changed} ModulusKit.* pin(s) to {targetVersion}.");
        console.WriteLine("Run 'dotnet restore' to pull the new packages, then 'modulus doctor' to verify the solution.");

        return Task.FromResult(failures.Count > 0 ? 1 : 0);
    }
}
