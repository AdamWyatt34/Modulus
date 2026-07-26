using System.Xml;
using System.Xml.Linq;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddConsumerHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(
        string eventName,
        string moduleName,
        string? solutionPath,
        string? eventModule,
        bool dryRun = false)
    {
        if (!CSharpIdentifierValidator.IsValid(eventName))
        {
            console.WriteError($"'{eventName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (eventModule is not null && !CSharpIdentifierValidator.IsValid(eventModule))
        {
            console.WriteError($"'{eventModule}' is not a valid C# identifier.");
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
        var solutionName = SolutionFinder.GetSolutionName(slnxPath);

        if (!solutionFinder.IsModulusSolution(solutionRoot, solutionName))
        {
            console.WriteError($"The solution at '{solutionRoot}' does not appear to be a Modulus solution.");
            return Task.FromResult(1);
        }

        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");
        var moduleDir = Path.Combine(modulesDir, moduleName);
        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' was not found at '{moduleDir}'. Run 'modulus add-module {moduleName}' first.");
            return Task.FromResult(1);
        }

        var infrastructureDir = Path.Combine(moduleDir, "src", $"{moduleName}.Infrastructure");
        if (!fileSystem.DirectoryExists(infrastructureDir))
        {
            console.WriteError($"The '{moduleName}.Infrastructure' project was not found at '{infrastructureDir}'.");
            return Task.FromResult(1);
        }

        // Validate the project file the consumer must be wired into BEFORE generating anything.
        // The handler is useless without the cross-module reference, so a missing or malformed
        // csproj is a hard failure rather than a silent no-op that ships an un-compilable handler.
        var csprojPath = Path.Combine(infrastructureDir, $"{moduleName}.Infrastructure.csproj");
        if (!fileSystem.FileExists(csprojPath))
        {
            console.WriteError($"The '{moduleName}.Infrastructure' project file was not found at '{csprojPath}'. The consumer cannot be wired to the event's Integration project.");
            return Task.FromResult(1);
        }

        var csprojText = fileSystem.ReadAllText(csprojPath);
        try
        {
            _ = XDocument.Parse(csprojText);
        }
        catch (XmlException ex)
        {
            console.WriteError($"Cannot wire the cross-module reference: '{csprojPath}' is not a well-formed project file ({ex.Message}).");
            return Task.FromResult(1);
        }

        if (!csprojText.Contains("</Project>", StringComparison.Ordinal))
        {
            console.WriteError($"Cannot wire the cross-module reference: '{csprojPath}' has no closing </Project> tag to insert into.");
            return Task.FromResult(1);
        }

        // Locate the event in the candidate modules' Integration projects.
        var candidates = ResolveCandidateModules(modulesDir, eventModule);
        var matches = new List<(string SourceModule, string FilePath)>();

        foreach (var candidate in candidates)
        {
            var integrationDir = Path.Combine(modulesDir, candidate, "src", $"{candidate}.Integration");
            if (!fileSystem.DirectoryExists(integrationDir))
            {
                continue;
            }

            // FakeFileSystem.GetFiles only honors '*'-prefixed extension patterns, so search by
            // extension and filter by file name in-process for cross-implementation correctness.
            foreach (var file in fileSystem.GetFiles(integrationDir, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(fileSystem.GetFileName(file), $"{eventName}.cs", StringComparison.Ordinal))
                {
                    matches.Add((candidate, file));
                }
            }
        }

        if (matches.Count == 0)
        {
            var scope = eventModule is null ? "any module's Integration project" : $"the '{eventModule}.Integration' project";
            console.WriteError($"Integration event '{eventName}' was not found in {scope}. Create it first with 'modulus add-event {eventName} --module <ModuleName>', or pass --event-module to point at its source module.");
            return Task.FromResult(1);
        }

        if (matches.Count > 1)
        {
            var found = string.Join(", ", matches.Select(m => m.SourceModule));
            console.WriteError($"Integration event '{eventName}' was found in multiple modules ({found}). Use --event-module to disambiguate.");
            return Task.FromResult(1);
        }

        var (sourceModule, eventFilePath) = matches[0];

        // The event file existing is not enough: the ProjectReference we are about to write
        // targets the source module's Integration *.csproj. If that project file is absent
        // (a partially generated or hand-damaged module), fail before any writes rather than
        // wiring a reference to a non-existent project and shipping a broken build.
        var sourceIntegrationCsproj = Path.Combine(
            modulesDir, sourceModule, "src", $"{sourceModule}.Integration", $"{sourceModule}.Integration.csproj");
        if (!fileSystem.FileExists(sourceIntegrationCsproj))
        {
            console.WriteError($"The '{sourceModule}.Integration' project file was not found at '{sourceIntegrationCsproj}'. The event '{eventName}' exists, but its Integration project is missing, so the reference cannot be wired.");
            return Task.FromResult(1);
        }

        var eventNamespace = ExtractNamespace(fileSystem.ReadAllText(eventFilePath))
            ?? $"{solutionName}.{sourceModule}.Integration.IntegrationEvents";

        var handlerFilePath = Path.Combine(infrastructureDir, "IntegrationEventHandlers", $"{eventName}Handler.cs");
        var handlerExists = fileSystem.FileExists(handlerFilePath);

        if (dryRun)
        {
            // Read-only equivalent of AddIntegrationProjectReference's own check — this previews
            // the real run's decision without writing the csproj.
            var wouldAddReference = !ProjectReferenceEditor.HasReferenceTo(csprojText, $"{sourceModule}.Integration.csproj");

            if (handlerExists && !wouldAddReference)
            {
                // Mirrors the real run's own failure path below: an existing, already-wired
                // handler is a hard error whether or not --dry-run was passed.
                console.WriteError($"Consumer '{eventName}Handler' already exists at '{handlerFilePath}'.");
                return Task.FromResult(1);
            }

            console.WriteLine("Dry run — no files were written. The following would happen:");

            if (!handlerExists)
            {
                console.WriteLine($"  create  {handlerFilePath}  -- {eventName}Handler : IIntegrationEventHandler<{eventName}>");
            }

            if (wouldAddReference)
            {
                console.WriteLine($"  edit    {csprojPath}  -- add ProjectReference to {sourceModule}.Integration");
            }

            console.WriteLine("Re-run without --dry-run to apply.");
            return Task.FromResult(0);
        }

        // Wire the cross-module reference first so a later failure can never leave an
        // un-referenced (non-compiling) handler behind. The operation is idempotent: it
        // returns false only when the reference is already present.
        var referenceAdded = AddIntegrationProjectReference(csprojPath, sourceModule);

        if (handlerExists)
        {
            // A prior run wrote the handler but not the reference: repair it instead of
            // dead-ending on the duplicate check, which would force manual recovery.
            if (referenceAdded)
            {
                console.WriteSuccess($"Consumer '{eventName}Handler' already existed; repaired its missing project reference.");
                console.WriteLine($"  Added project reference: {moduleName}.Infrastructure -> {sourceModule}.Integration");
                return Task.FromResult(0);
            }

            console.WriteError($"Consumer '{eventName}Handler' already exists at '{handlerFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new ConsumerGenerator();
        var output = generator.Generate(new ConsumerOptions
        {
            EventName = eventName,
            EventNamespace = eventNamespace,
            ModuleName = moduleName,
            SolutionName = solutionName,
        });

        var moduleRoot = Path.Combine("src", "Modules", moduleName);
        var remappedPath = Path.Combine(moduleRoot, output.RelativePath);
        var fullPath = PathGuard.EnsureContained(solutionRoot, remappedPath);
        var dir = fileSystem.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Could not determine directory for path: {fullPath}");
        fileSystem.CreateDirectory(dir);
        fileSystem.WriteAllText(fullPath, output.Content);

        console.WriteSuccess($"Consumer '{eventName}Handler' added to module '{moduleName}'.");
        console.WriteLine($"  Handles: {eventName} (from '{sourceModule}.Integration')");

        if (referenceAdded)
        {
            console.WriteLine($"  Added project reference: {moduleName}.Infrastructure -> {sourceModule}.Integration");
        }

        console.WriteLine("");
        console.WriteLine("  Next steps:");
        console.WriteLine($"    1. Implement the handling logic in {eventName}Handler");
        console.WriteLine($"    2. Ensure the '{moduleName}.Infrastructure' assembly is registered for messaging:");
        console.WriteLine("       AddModulusMessaging(builder.Configuration, o => o.Assemblies.Add(typeof(<a type in the module>).Assembly))");

        return Task.FromResult(0);
    }

    private IReadOnlyList<string> ResolveCandidateModules(string modulesDir, string? eventModule)
    {
        if (eventModule is not null)
        {
            return [eventModule];
        }

        if (!fileSystem.DirectoryExists(modulesDir))
        {
            return [];
        }

        return fileSystem.GetDirectories(modulesDir)
            .Select(fileSystem.GetFileName)
            .ToList();
    }

    /// <summary>
    /// Adds a <c>ProjectReference</c> from the consuming module's Infrastructure project (at
    /// <paramref name="csprojPath"/>) to the source module's Integration project. This is the
    /// MOD001-compliant cross-module reference (Integration projects are the only permitted
    /// cross-module dependency target). Returns <c>true</c> when a reference was added,
    /// <c>false</c> when an actual <c>ProjectReference</c> to the source project already exists.
    /// Idempotency and the edit itself are delegated to <see cref="ProjectReferenceEditor"/> so
    /// every handler that wires a cross-project reference resolves "is it already there?" and
    /// applies the edit the same way. The caller guarantees the file exists, parses as XML, and
    /// contains a closing tag.
    /// </summary>
    private bool AddIntegrationProjectReference(string csprojPath, string sourceModule)
    {
        var content = fileSystem.ReadAllText(csprojPath);
        var expectedCsprojFileName = $"{sourceModule}.Integration.csproj";

        if (ProjectReferenceEditor.HasReferenceTo(content, expectedCsprojFileName))
        {
            return false;
        }

        if (!content.Contains("</Project>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot add a project reference: '{csprojPath}' has no closing </Project> tag.");
        }

        var relativeReference = $"..\\..\\..\\{sourceModule}\\src\\{sourceModule}.Integration\\{sourceModule}.Integration.csproj";
        fileSystem.WriteAllText(csprojPath, ProjectReferenceEditor.AddReference(content, relativeReference));
        return true;
    }

    /// <summary>
    /// Extracts the declared namespace from a C# source file, supporting both file-scoped and
    /// block-scoped namespace declarations. Returns <c>null</c> when no namespace is found.
    /// </summary>
    private static string? ExtractNamespace(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("namespace ", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["namespace ".Length..].Trim();
            var terminator = value.IndexOfAny([';', '{', ' ']);
            if (terminator >= 0)
            {
                value = value[..terminator];
            }

            return value.Length > 0 ? value : null;
        }

        return null;
    }
}
