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
        bool noEndpoints)
    {
        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return 1;
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))!;
        var solutionName = SolutionFinder.GetSolutionName(slnxPath);

        if (!solutionFinder.IsModulusSolution(solutionRoot, solutionName))
        {
            console.WriteError($"The solution at '{solutionRoot}' does not appear to be a Modulus solution (ModuleRegistration.cs not found in {solutionName}.WebApi).");
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
        var csprojPaths = new List<string>();
        var fileCount = 0;

        foreach (var output in filtered)
        {
            var remappedPath = Path.Combine(moduleRoot, output.RelativePath);
            var fullPath = Path.Combine(solutionRoot, remappedPath);
            var dir = fileSystem.GetDirectoryName(fullPath)!;
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, output.Content);
            fileCount++;

            if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                csprojPaths.Add(fullPath);
            }
        }

        console.WriteLine($"Created module '{moduleName}' with {fileCount} files.");

        await AddProjectsToSolution(slnxPath, solutionRoot, moduleName, csprojPaths);

        UpdateModuleRegistration(solutionRoot, solutionName, moduleName, noEndpoints);

        var restoreResult = await processRunner.RunAsync("dotnet", "restore", solutionRoot);
        if (restoreResult != 0)
        {
            console.WriteError($"Warning: dotnet restore failed with exit code {restoreResult}. You may need to run it manually.");
        }

        console.WriteSuccess($"Module '{moduleName}' added successfully.");
        console.WriteLine($"  Projects: {csprojPaths.Count}");
        console.WriteLine($"  Endpoints: {(noEndpoints ? "Skipped" : "Included")}");

        return 0;
    }

    private async Task AddProjectsToSolution(
        string slnxPath,
        string solutionRoot,
        string moduleName,
        List<string> csprojPaths)
    {
        var fullSlnxPath = fileSystem.GetFullPath(slnxPath);

        foreach (var csproj in csprojPaths)
        {
            var isTestProject = csproj.Contains("tests", StringComparison.OrdinalIgnoreCase);

            var solutionFolder = isTestProject
                ? $"/tests/Modules/{moduleName}/"
                : $"/src/Modules/{moduleName}/";

            var result = await processRunner.RunAsync(
                "dotnet",
                $"sln \"{fullSlnxPath}\" add \"{csproj}\" --solution-folder \"{solutionFolder}\"",
                solutionRoot);

            if (result != 0)
            {
                console.WriteError($"Warning: Failed to add '{fileSystem.GetFileName(csproj)}' to solution.");
            }
        }
    }

    private void UpdateModuleRegistration(
        string solutionRoot,
        string solutionName,
        string moduleName,
        bool noEndpoints)
    {
        var registrationPath = Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", "ModuleRegistration.cs");

        if (!fileSystem.FileExists(registrationPath))
        {
            console.WriteError("Warning: Could not find ModuleRegistration.cs. Please register the module manually.");
            PrintManualRegistration(moduleName, solutionName, noEndpoints);
            return;
        }

        var content = fileSystem.ReadAllText(registrationPath);

        var usings = $"using {solutionName}.{moduleName}.Infrastructure;\n";
        if (!noEndpoints)
        {
            usings += $"using {solutionName}.{moduleName}.Api.Endpoints;\n";
        }

        var namespaceIndex = content.IndexOf("namespace ", StringComparison.Ordinal);
        if (namespaceIndex >= 0)
        {
            content = content.Insert(namespaceIndex, usings);
        }
        else
        {
            console.WriteError("Warning: Could not find namespace declaration in ModuleRegistration.cs. Please add usings manually.");
        }

        var returnServicesIndex = content.IndexOf("return services;", StringComparison.Ordinal);
        if (returnServicesIndex >= 0)
        {
            var addModuleLine = $"        services.Add{moduleName}Module(configuration);\n\n        ";
            content = content.Insert(returnServicesIndex, addModuleLine);
        }
        else
        {
            console.WriteError("Warning: Could not find 'return services;' in ModuleRegistration.cs.");
            PrintManualRegistration(moduleName, solutionName, noEndpoints);
            return;
        }

        if (!noEndpoints)
        {
            var returnAppIndex = content.IndexOf("return app;", StringComparison.Ordinal);
            if (returnAppIndex >= 0)
            {
                var mapEndpointsLine = $"        app.Map{moduleName}Endpoints();\n\n        ";
                content = content.Insert(returnAppIndex, mapEndpointsLine);
            }
            else
            {
                console.WriteError("Warning: Could not find 'return app;' in ModuleRegistration.cs.");
            }
        }

        fileSystem.WriteAllText(registrationPath, content);
    }

    private void PrintManualRegistration(string moduleName, string solutionName, bool noEndpoints)
    {
        console.WriteLine($"  Add to ModuleRegistration.cs:");
        console.WriteLine($"    using {solutionName}.{moduleName}.Infrastructure;");
        console.WriteLine($"    services.Add{moduleName}Module(configuration);");
        if (!noEndpoints)
        {
            console.WriteLine($"    using {solutionName}.{moduleName}.Api.Endpoints;");
            console.WriteLine($"    app.Map{moduleName}Endpoints();");
        }
    }

    internal static string StripApiReferencesFromArchTests(string content)
    {
        var lines = content.Split('\n').ToList();

        lines.RemoveAll(l => l.TrimStart().StartsWith("using") && l.Contains(".Api.Endpoints"));
        lines.RemoveAll(l => l.Contains("ApiAssembly"));

        RemoveTestMethod(lines, "Domain_should_not_depend_on_Api");
        RemoveTestMethod(lines, "Application_should_not_depend_on_Api");
        RemoveTestMethod(lines, "Infrastructure_should_not_depend_on_Api");

        return string.Join('\n', lines);
    }

    internal static string StripApiReferencesFromModuleClass(string content)
    {
        var lines = content.Split('\n').ToList();

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
        var lines = csprojContent.Split('\n').ToList();
        lines.RemoveAll(l => l.Contains($"{moduleName}.Api"));
        return string.Join('\n', lines);
    }
}
