using System.Xml;
using System.Xml.Linq;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddModuleHandler(
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public async Task<int> ExecuteAsync(
        string moduleName,
        string? solutionPath,
        bool noEndpoints,
        bool dryRun = false,
        bool noRestore = false)
    {
        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return 1;
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError(solutionFinder.DescribeResolutionFailure(solutionPath));
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))
            ?? throw new InvalidOperationException($"Could not determine directory for path: {slnxPath}");
        var solutionName = SolutionFinder.GetSolutionName(slnxPath);

        if (!solutionFinder.IsModulusSolution(solutionRoot, solutionName))
        {
            console.WriteError($"The solution at '{solutionRoot}' does not appear to be a Modulus solution (Program.cs not found in {solutionName}.WebApi).");
            return 1;
        }

        var moduleDir = Path.Combine(solutionRoot, "src", "Modules", moduleName);
        if (fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' already exists at '{moduleDir}'.");
            return 1;
        }

        var engine = new TemplateEngine();
        var outputs = engine.GenerateModule(new ModuleOptions
        {
            ModuleName = moduleName,
            SolutionName = solutionName,
        });

        var filtered = new List<TemplateOutput>(outputs);

        if (noEndpoints)
        {
            filtered.RemoveAll(o => o.RelativePath.Contains($".Api{Path.DirectorySeparatorChar}")
                || o.RelativePath.Contains(".Api/"));

            // H-CLI4: without an Api project the module maps zero HTTP endpoints, so the
            // scaffolded integration test that GETs one would be red out of the box (or, at
            // best, meaningless) — exclude it the same way the Api project's own files are
            // excluded above. This file doesn't live under a ".Api/" path, so the removal above
            // never catches it.
            filtered.RemoveAll(o => o.RelativePath.EndsWith($"{moduleName}EndpointTests.cs", StringComparison.OrdinalIgnoreCase));

            for (var i = 0; i < filtered.Count; i++)
            {
                var output = filtered[i];

                if (output.RelativePath.EndsWith("LayerDependencyTests.cs", StringComparison.OrdinalIgnoreCase))
                {
                    filtered[i] = output with { Content = StripApiReferencesFromArchTests(output.Content) };
                }

                if (output.RelativePath.EndsWith("Module.cs", StringComparison.OrdinalIgnoreCase)
                    && output.Content.Contains(".Api.Endpoints"))
                {
                    filtered[i] = output with { Content = StripApiReferencesFromModuleClass(output.Content) };
                }

                if (output.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    && output.Content.Contains($"{moduleName}.Api"))
                {
                    filtered[i] = filtered[i] with { Content = RemoveApiProjectReference(filtered[i].Content, moduleName) };
                }
            }
        }

        var moduleRoot = Path.Combine("src", "Modules", moduleName);
        var plannedFiles = filtered
            .Select(output => (Output: output, FullPath: PathGuard.EnsureContained(solutionRoot, Path.Combine(moduleRoot, output.RelativePath))))
            .ToList();

        if (dryRun)
        {
            console.WriteLine($"Dry run — no files were written and no processes were run. The following would happen for module '{moduleName}':");

            foreach (var (_, fullPath) in plannedFiles)
            {
                console.WriteLine($"  create  {fullPath}");
            }

            var (hostCsprojPath, wouldAddHostReference) = PreviewHostProjectReference(solutionRoot, solutionName, moduleName);
            if (wouldAddHostReference)
            {
                console.WriteLine($"  edit    {hostCsprojPath}  -- add ProjectReference to {moduleName}.Infrastructure (required for module discovery)");
            }

            var csprojCount = plannedFiles.Count(p => p.FullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (csprojCount > 0)
            {
                console.WriteLine($"  run     dotnet sln add  ({csprojCount} project(s), in {solutionRoot})");
            }

            console.WriteLine(!noRestore
                ? $"  run     dotnet restore  (in {solutionRoot})"
                : "  skip    dotnet restore  (--no-restore)");

            console.WriteLine("Re-run without --dry-run to apply.");
            return 0;
        }

        var csprojEntries = new List<(string FullPath, bool IsTestProject)>();
        var fileCount = 0;

        foreach (var (output, fullPath) in plannedFiles)
        {
            var dir = fileSystem.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Could not determine directory for path: {fullPath}");
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, output.Content);
            fileCount++;

            if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                csprojEntries.Add((fullPath, IsTestProjectOutput(output.RelativePath)));
            }
        }

        console.WriteLine($"Created module '{moduleName}' with {fileCount} files.");

        // Module discovery (ModuleRegistrationGenerator / HandlerRegistrationGenerator) scans the
        // host's *referenced assemblies* at compile time — adding the module to the .slnx alone
        // never makes the host build against it. Without this ProjectReference the module builds
        // but its ConfigureServices/handlers/endpoints never run.
        var hostReferenceAdded = AddHostProjectReference(solutionRoot, solutionName, moduleName);

        await AddProjectsToSolution(slnxPath, solutionRoot, moduleName, csprojEntries);

        var restoreStatus = "Skipped (--no-restore)";
        if (!noRestore)
        {
            var restoreResult = await processRunner.RunAsync("dotnet", ["restore"], solutionRoot);
            if (restoreResult != 0)
            {
                console.WriteError($"Warning: dotnet restore failed with exit code {restoreResult}. You may need to run it manually.");
                restoreStatus = $"Failed (exit code {restoreResult})";
            }
            else
            {
                restoreStatus = "OK";
            }
        }

        console.WriteSuccess($"Module '{moduleName}' added successfully.");
        console.WriteLine($"  Projects: {csprojEntries.Count}");
        console.WriteLine($"  Endpoints: {(noEndpoints ? "Skipped" : "Included")}");
        console.WriteLine($"  Restore: {restoreStatus}");

        if (hostReferenceAdded)
        {
            console.WriteLine($"  Host wiring: added ProjectReference {solutionName}.WebApi -> {moduleName}.Infrastructure (required for module discovery)");
        }

        return 0;
    }

    /// <summary>
    /// Read-only preview of what <see cref="AddHostProjectReference"/> would do, for
    /// <c>--dry-run</c>: same detection (file exists, well-formed XML, has a closing tag, not
    /// already referenced) but never writes and never emits warnings — a dry run should describe
    /// a clean plan, not the failure-path diagnostics of the real edit.
    /// </summary>
    private (string HostCsprojPath, bool WouldAdd) PreviewHostProjectReference(string solutionRoot, string solutionName, string moduleName)
    {
        var hostCsprojPath = Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", $"{solutionName}.WebApi.csproj");
        var infrastructureCsprojFileName = $"{moduleName}.Infrastructure.csproj";

        if (!fileSystem.FileExists(hostCsprojPath))
        {
            return (hostCsprojPath, false);
        }

        var content = fileSystem.ReadAllText(hostCsprojPath);

        try
        {
            _ = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return (hostCsprojPath, false);
        }

        if (!content.Contains("</Project>", StringComparison.Ordinal))
        {
            return (hostCsprojPath, false);
        }

        return (hostCsprojPath, !ProjectReferenceEditor.HasReferenceTo(content, infrastructureCsprojFileName));
    }

    /// <summary>
    /// Wires a <c>ProjectReference</c> from <c>&lt;Solution&gt;.WebApi.csproj</c> to the new
    /// module's Infrastructure project. Module discovery is compile-time over the host's
    /// referenced assemblies, so this reference — not the .slnx entry — is what makes the
    /// scaffolded module's <c>ConfigureServices</c>, handlers, and endpoints actually run.
    /// Uses the same XML-parsed, idempotent edit as <c>AddConsumerHandler</c>
    /// (<see cref="ProjectReferenceEditor"/>): detection inspects parsed <c>ProjectReference</c>
    /// elements so a repeat/repair run never double-wires. Failures here are reported as warnings
    /// rather than aborting — every module file has already been written to disk by this point,
    /// so failing the whole command would misreport a mostly-successful scaffold.
    /// </summary>
    private bool AddHostProjectReference(string solutionRoot, string solutionName, string moduleName)
    {
        var hostCsprojPath = Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", $"{solutionName}.WebApi.csproj");
        var infrastructureCsprojFileName = $"{moduleName}.Infrastructure.csproj";

        if (!fileSystem.FileExists(hostCsprojPath))
        {
            console.WriteError($"Warning: host project file was not found at '{hostCsprojPath}'. Add a ProjectReference to '{infrastructureCsprojFileName}' manually so the host discovers the module.");
            return false;
        }

        var content = fileSystem.ReadAllText(hostCsprojPath);

        try
        {
            _ = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            console.WriteError($"Warning: '{hostCsprojPath}' is not well-formed XML ({ex.Message}); could not wire the module into the host. Add a ProjectReference to '{infrastructureCsprojFileName}' manually.");
            return false;
        }

        if (!content.Contains("</Project>", StringComparison.Ordinal))
        {
            console.WriteError($"Warning: '{hostCsprojPath}' has no closing </Project> tag; could not wire the module into the host.");
            return false;
        }

        if (ProjectReferenceEditor.HasReferenceTo(content, infrastructureCsprojFileName))
        {
            return false; // Already wired — e.g. re-running after a partially-completed prior run.
        }

        var relativeReference = $"..\\Modules\\{moduleName}\\src\\{moduleName}.Infrastructure\\{infrastructureCsprojFileName}";
        fileSystem.WriteAllText(hostCsprojPath, ProjectReferenceEditor.AddReference(content, relativeReference));
        return true;
    }

    private async Task AddProjectsToSolution(
        string slnxPath,
        string solutionRoot,
        string moduleName,
        List<(string FullPath, bool IsTestProject)> csprojEntries)
    {
        var fullSlnxPath = fileSystem.GetFullPath(slnxPath);

        foreach (var (csproj, isTestProject) in csprojEntries)
        {
            var solutionFolder = isTestProject
                ? $"/tests/Modules/{moduleName}/"
                : $"/src/Modules/{moduleName}/";

            var result = await processRunner.RunAsync(
                "dotnet",
                ["sln", fullSlnxPath, "add", csproj, "--solution-folder", solutionFolder],
                solutionRoot);

            if (result != 0)
            {
                console.WriteError($"Warning: Failed to add '{fileSystem.GetFileName(csproj)}' to solution.");
            }
        }
    }

    /// <summary>
    /// Classifies a module-relative template output path (e.g.
    /// <c>tests/Catalog.Tests.Unit/Catalog.Tests.Unit.csproj</c> vs.
    /// <c>src/Catalog.Domain/Catalog.Domain.csproj</c>) as a test project by checking whether its
    /// *first path segment* is "tests" — never by substring-matching the full absolute path.
    /// A raw <c>Contains("tests")</c> over the whole path misfiles a module whose name contains
    /// "tests" (e.g. "Contests") or any solution checked out under a directory whose name
    /// contains "tests", because the substring shows up outside the path segment that actually
    /// distinguishes src from tests.
    /// </summary>
    internal static bool IsTestProjectOutput(string moduleRelativePath)
    {
        var normalized = moduleRelativePath.Replace('\\', '/');
        var firstSlash = normalized.IndexOf('/');
        var firstSegment = firstSlash >= 0 ? normalized[..firstSlash] : normalized;
        return string.Equals(firstSegment, "tests", StringComparison.OrdinalIgnoreCase);
    }

    internal static string StripApiReferencesFromArchTests(string content)
    {
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        lines.RemoveAll(l => l.TrimStart().StartsWith("using") && l.Contains(".Api.Endpoints"));
        lines.RemoveAll(l => l.Contains("ApiAssembly"));

        RemoveTestMethod(lines, "Domain_should_not_depend_on_Api");
        RemoveTestMethod(lines, "Application_should_not_depend_on_Api");
        RemoveTestMethod(lines, "Infrastructure_should_not_depend_on_Api");

        return string.Join('\n', lines);
    }

    internal static string StripApiReferencesFromModuleClass(string content)
    {
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        lines.RemoveAll(l => l.TrimStart().StartsWith("using") && l.Contains(".Api.Endpoints"));

        // Replace ConfigureEndpoints body with a pass-through
        var methodIndex = lines.FindIndex(l => l.Contains("ConfigureEndpoints"));
        if (methodIndex >= 0)
        {
            // Find the opening brace of the method
            var openBrace = lines.FindIndex(methodIndex, l => l.Contains('{'));
            if (openBrace >= 0)
            {
                // Find the matching closing brace
                var braceCount = 0;
                var closeBrace = openBrace;
                for (var i = openBrace; i < lines.Count; i++)
                {
                    braceCount += lines[i].Count(c => c == '{');
                    braceCount -= lines[i].Count(c => c == '}');
                    if (braceCount <= 0 && lines[i].Contains('}'))
                    {
                        closeBrace = i;
                        break;
                    }
                }

                // Replace the body between braces with a pass-through
                var indent = "        ";
                var replacement = new List<string>
                {
                    lines[openBrace], // Keep the opening brace
                    $"{indent}return endpoints;",
                    lines[closeBrace] // Keep the closing brace
                };

                lines.RemoveRange(openBrace, closeBrace - openBrace + 1);
                lines.InsertRange(openBrace, replacement);
            }
        }

        return string.Join('\n', lines);
    }

    private static void RemoveTestMethod(List<string> lines, string methodName)
    {
        var startIndex = lines.FindIndex(l => l.Contains(methodName));
        if (startIndex < 0) return;

        var factIndex = startIndex;
        while (factIndex > 0 && !lines[factIndex].TrimStart().StartsWith("[Fact]"))
            factIndex--;

        var braceCount = 0;
        var endIndex = startIndex;
        for (var i = startIndex; i < lines.Count; i++)
        {
            braceCount += lines[i].Count(c => c == '{');
            braceCount -= lines[i].Count(c => c == '}');
            if (braceCount <= 0 && lines[i].Contains('}'))
            {
                endIndex = i;
                break;
            }
        }

        // Also remove any blank line after the method
        if (endIndex + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[endIndex + 1]))
        {
            endIndex++;
        }

        lines.RemoveRange(factIndex, endIndex - factIndex + 1);
    }

    internal static string RemoveApiProjectReference(string csprojContent, string moduleName)
    {
        var lines = csprojContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lines.RemoveAll(l => l.Contains("ProjectReference") && l.Contains($"{moduleName}.Api"));
        return string.Join('\n', lines);
    }
}
