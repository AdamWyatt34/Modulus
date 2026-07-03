using System.Text.Json;
using System.Xml.Linq;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

public sealed class DoctorHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(string? solutionPath, bool json, bool strict)
    {
        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
            return Task.FromResult(1);
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))
            ?? throw new InvalidOperationException($"Could not determine directory for path: {slnxPath}");

        var results = new List<List<DoctorCheckResult>>
        {
            CheckSolutionShape(solutionRoot, slnxPath),
            CheckPackageVersions(solutionRoot),
            CheckModuleArtifacts(solutionRoot),
            CheckMessagingConfig(solutionRoot),
            CheckProjectReferences(solutionRoot),
            CheckMigrationGuidance(solutionRoot),
        }.SelectMany(r => r).ToList();

        var passCount = results.Count(r => r.Status == DoctorStatus.Pass);
        var warnCount = results.Count(r => r.Status == DoctorStatus.Warn);
        var failCount = results.Count(r => r.Status == DoctorStatus.Fail);

        if (json)
        {
            WriteJson(results, passCount, warnCount, failCount);
        }
        else
        {
            WriteHuman(results, passCount, warnCount, failCount);
        }

        if (failCount > 0)
            return Task.FromResult(1);

        if (strict && warnCount > 0)
            return Task.FromResult(2);

        return Task.FromResult(0);
    }

    private void WriteHuman(List<DoctorCheckResult> results, int passCount, int warnCount, int failCount)
    {
        foreach (var result in results)
        {
            var line = $"[{StatusLabel(result.Status)}] {result.CheckName}: {result.Message}";
            switch (result.Status)
            {
                case DoctorStatus.Pass:
                    console.WriteSuccess(line);
                    break;
                case DoctorStatus.Fail:
                    console.WriteError(line);
                    break;
                default:
                    console.WriteLine(line);
                    break;
            }
        }

        console.WriteLine("");
        console.WriteLine($"Summary: {passCount} passed, {warnCount} warning(s), {failCount} failed.");
    }

    private void WriteJson(List<DoctorCheckResult> results, int passCount, int warnCount, int failCount)
    {
        var document = new
        {
            checks = results.Select(r => new
            {
                name = r.CheckName,
                status = r.Status.ToString(),
                message = r.Message,
            }),
            summary = new
            {
                pass = passCount,
                warn = warnCount,
                fail = failCount,
            },
        };

        console.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StatusLabel(DoctorStatus status) => status switch
    {
        DoctorStatus.Pass => "PASS",
        DoctorStatus.Warn => "WARN",
        DoctorStatus.Fail => "FAIL",
        _ => status.ToString().ToUpperInvariant(),
    };

    // ── Check 1: Solution shape ──────────────────────────────────

    private List<DoctorCheckResult> CheckSolutionShape(string solutionRoot, string slnxPath)
    {
        const string checkName = "SolutionShape";

        if (!fileSystem.FileExists(slnxPath))
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Fail, $"No .slnx solution file found at '{slnxPath}'.")];
        }

        var srcDir = Path.Combine(solutionRoot, "src");
        if (!fileSystem.DirectoryExists(srcDir))
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Fail, $"Expected top-level 'src' folder was not found at '{srcDir}'.")];
        }

        return [new DoctorCheckResult(checkName, DoctorStatus.Pass, $"Solution file and 'src' folder found at '{solutionRoot}'.")];
    }

    // ── Check 2: Package versions ────────────────────────────────

    private List<DoctorCheckResult> CheckPackageVersions(string solutionRoot)
    {
        const string checkName = "PackageVersions";
        var propsPath = Path.Combine(solutionRoot, "Directory.Packages.props");

        if (!fileSystem.FileExists(propsPath))
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Fail, $"'Directory.Packages.props' was not found at '{propsPath}'.")];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(fileSystem.ReadAllText(propsPath));
        }
        catch (Exception ex)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Fail, $"'{propsPath}' is not well-formed XML: {ex.Message}")];
        }

        var modulusKitVersions = document.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => (Include: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version")))
            .Where(p => p.Include is not null && p.Include.StartsWith("ModulusKit.", StringComparison.Ordinal))
            .ToList();

        if (modulusKitVersions.Count == 0)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, "No ModulusKit.* package references found.")];
        }

        var distinctVersions = modulusKitVersions
            .Select(p => p.Version)
            .Distinct()
            .ToList();

        if (distinctVersions.Count <= 1)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, $"All {modulusKitVersions.Count} ModulusKit.* package(s) share version '{distinctVersions.FirstOrDefault()}'.")];
        }

        var byVersion = modulusKitVersions
            .GroupBy(p => p.Version)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(p => p.Include))}");
        var detail = string.Join("; ", byVersion);

        return [new DoctorCheckResult(checkName, DoctorStatus.Warn, $"ModulusKit.* packages have inconsistent versions ({detail}).")];
    }

    // ── Check 3: Module artifacts ────────────────────────────────

    private List<DoctorCheckResult> CheckModuleArtifacts(string solutionRoot)
    {
        const string checkName = "ModuleArtifacts";
        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");

        if (!fileSystem.DirectoryExists(modulesDir))
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, "No 'src/Modules' directory found; skipping module artifact checks.")];
        }

        var moduleDirs = fileSystem.GetDirectories(modulesDir);
        if (moduleDirs.Count == 0)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, "No modules found under 'src/Modules'.")];
        }

        var results = new List<DoctorCheckResult>();
        string[] expectedLayers = ["Domain", "Application", "Infrastructure", "Integration"];

        foreach (var moduleDir in moduleDirs)
        {
            var moduleName = fileSystem.GetFileName(moduleDir);
            var moduleSrcDir = Path.Combine(moduleDir, "src");
            var missing = new List<string>();

            foreach (var layer in expectedLayers)
            {
                var projectName = $"{moduleName}.{layer}";
                var csprojPath = Path.Combine(moduleSrcDir, projectName, $"{projectName}.csproj");
                if (!fileSystem.FileExists(csprojPath))
                {
                    missing.Add(projectName);
                }
            }

            if (missing.Count == 0)
            {
                results.Add(new DoctorCheckResult(checkName, DoctorStatus.Pass, $"Module '{moduleName}' has all expected projects."));
            }
            else
            {
                results.Add(new DoctorCheckResult(checkName, DoctorStatus.Warn, $"Module '{moduleName}' is missing expected project(s): {string.Join(", ", missing)}."));
            }
        }

        return results;
    }

    // ── Check 4: Messaging config ────────────────────────────────

    private List<DoctorCheckResult> CheckMessagingConfig(string solutionRoot)
    {
        const string checkName = "MessagingConfig";
        var srcDir = Path.Combine(solutionRoot, "src");

        if (!fileSystem.DirectoryExists(srcDir))
        {
            return [];
        }

        var csprojFiles = fileSystem.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories);
        var usesMessaging = csprojFiles.Any(csproj =>
            fileSystem.ReadAllText(csproj).Contains("ModulusKit.Messaging", StringComparison.Ordinal));

        if (!usesMessaging)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, "No project references ModulusKit.Messaging; skipping messaging configuration check.")];
        }

        var appsettingsPath = FindWebApiAppSettings(srcDir);
        if (appsettingsPath is null)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Warn, "ModulusKit.Messaging is referenced but no WebApi 'appsettings.json' was found to validate the Messaging section.")];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(fileSystem.ReadAllText(appsettingsPath));
        }
        catch (JsonException ex)
        {
            return [new DoctorCheckResult(checkName, DoctorStatus.Warn, $"'{appsettingsPath}' could not be parsed as JSON: {ex.Message}")];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("Messaging", out var messaging))
            {
                return [new DoctorCheckResult(checkName, DoctorStatus.Warn, $"'{appsettingsPath}' is missing a 'Messaging' section, but ModulusKit.Messaging is referenced.")];
            }

            var transportValue = messaging.TryGetProperty("Transport", out var transportElement)
                ? transportElement.GetString()
                : null;

            if (transportValue is null || !ValidTransports.Contains(transportValue))
            {
                return [new DoctorCheckResult(checkName, DoctorStatus.Warn, $"'{appsettingsPath}' Messaging:Transport is missing or invalid (expected one of InMemory, RabbitMq, AzureServiceBus).")];
            }

            if (string.Equals(transportValue, "InMemory", StringComparison.Ordinal))
            {
                return [new DoctorCheckResult(checkName, DoctorStatus.Pass, $"Messaging section found with Transport '{transportValue}'.")];
            }

            var connectionString = messaging.TryGetProperty("ConnectionString", out var csElement) ? csElement.GetString() : null;
            var fullyQualifiedNamespace = messaging.TryGetProperty("FullyQualifiedNamespace", out var nsElement) ? nsElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                return [new DoctorCheckResult(checkName, DoctorStatus.Warn, $"Messaging:Transport is '{transportValue}' but neither ConnectionString nor FullyQualifiedNamespace is set in '{appsettingsPath}'.")];
            }

            return [new DoctorCheckResult(checkName, DoctorStatus.Pass, $"Messaging section found with Transport '{transportValue}' and a connection configured.")];
        }
    }

    private static readonly HashSet<string> ValidTransports = new(StringComparer.Ordinal) { "InMemory", "RabbitMq", "AzureServiceBus" };

    private string? FindWebApiAppSettings(string srcDir)
    {
        foreach (var dir in fileSystem.GetDirectories(srcDir))
        {
            if (!fileSystem.GetFileName(dir).EndsWith(".WebApi", StringComparison.Ordinal))
                continue;

            var appsettingsPath = Path.Combine(dir, "appsettings.json");
            if (fileSystem.FileExists(appsettingsPath))
                return appsettingsPath;
        }

        return null;
    }

    // ── Check 5: Project references ──────────────────────────────

    private List<DoctorCheckResult> CheckProjectReferences(string solutionRoot)
    {
        const string checkName = "ProjectReferences";
        var results = new List<DoctorCheckResult>();
        var csprojFiles = fileSystem.GetFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories);

        foreach (var csprojPath in csprojFiles)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(fileSystem.ReadAllText(csprojPath));
            }
            catch (Exception)
            {
                // Malformed csproj files are out of scope for this check; other tooling will catch them at build time.
                continue;
            }

            var csprojDir = fileSystem.GetDirectoryName(csprojPath) ?? solutionRoot;

            var references = document.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include));

            foreach (var include in references)
            {
                var normalized = include!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(Path.Combine(csprojDir, normalized));

                if (!fileSystem.FileExists(targetPath))
                {
                    results.Add(new DoctorCheckResult(checkName, DoctorStatus.Fail, $"'{csprojPath}' references missing project '{include}'."));
                }
            }
        }

        if (results.Count == 0)
        {
            results.Add(new DoctorCheckResult(checkName, DoctorStatus.Pass, $"All ProjectReference entries across {csprojFiles.Count} project(s) resolve to existing files."));
        }

        return results;
    }

    // ── Check 6: Migration guidance ──────────────────────────────

    private List<DoctorCheckResult> CheckMigrationGuidance(string solutionRoot)
    {
        const string checkName = "MigrationGuidance";
        var srcDir = Path.Combine(solutionRoot, "src");

        if (!fileSystem.DirectoryExists(srcDir))
        {
            return [];
        }

        var programFiles = fileSystem.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => string.Equals(fileSystem.GetFileName(f), "Program.cs", StringComparison.Ordinal))
            .ToList();

        var results = new List<DoctorCheckResult>();

        foreach (var programFile in programFiles)
        {
            var content = fileSystem.ReadAllText(programFile);
            var usesOutboxOrInbox = content.Contains("AddModulusOutbox", StringComparison.Ordinal)
                || content.Contains("AddModulusInbox", StringComparison.Ordinal);

            if (!usesOutboxOrInbox)
            {
                continue;
            }

            var callsMigration = content.Contains("UseModulusMessagingMigrationsAsync", StringComparison.Ordinal);
            if (!callsMigration)
            {
                results.Add(new DoctorCheckResult(checkName, DoctorStatus.Warn, $"'{programFile}' registers AddModulusOutbox/AddModulusInbox but never calls UseModulusMessagingMigrationsAsync; pending outbox/inbox migrations will not be applied at startup."));
            }
        }

        if (results.Count == 0)
        {
            results.Add(new DoctorCheckResult(checkName, DoctorStatus.Pass, "No outbox/inbox registration missing migration guidance."));
        }

        return results;
    }
}

internal enum DoctorStatus
{
    Pass,
    Warn,
    Fail,
}

internal sealed record DoctorCheckResult(string CheckName, DoctorStatus Status, string Message);
