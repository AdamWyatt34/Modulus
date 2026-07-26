using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddEndpointHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    private static readonly HashSet<string> ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE",
    };

    private static readonly HashSet<string> ReservedLambdaParameterNames = new(StringComparer.Ordinal)
    {
        "mediator", "ct",
    };

    public Task<int> ExecuteAsync(
        string endpointName,
        string moduleName,
        string? solutionPath,
        string method,
        string route,
        string? commandName,
        string? queryName,
        string? resultType,
        bool dryRun = false)
    {
        if (!CSharpIdentifierValidator.IsValid(endpointName))
        {
            console.WriteError($"'{endpointName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (!ValidMethods.Contains(method))
        {
            console.WriteError($"'{method}' is not a supported HTTP method. Use GET, POST, PUT, or DELETE.");
            return Task.FromResult(1);
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(route, @"^[a-zA-Z0-9/{}_:?=\-\.]+$"))
        {
            console.WriteError($"Route '{route}' contains invalid characters.");
            return Task.FromResult(1);
        }

        var routeParams = RouteTemplateParser.Parse(route);
        var routeParamNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var routeParam in routeParams)
        {
            if (!CSharpIdentifierValidator.IsValid(routeParam.Name))
            {
                console.WriteError($"Route parameter '{{{routeParam.Name}}}' in '{route}' is not a valid C# identifier.");
                return Task.FromResult(1);
            }

            if (ReservedLambdaParameterNames.Contains(routeParam.Name))
            {
                console.WriteError($"Route parameter '{{{routeParam.Name}}}' in '{route}' collides with the generated lambda's own '{routeParam.Name}' parameter. Rename it.");
                return Task.FromResult(1);
            }

            if (!routeParamNames.Add(routeParam.Name))
            {
                console.WriteError($"Route parameter '{{{routeParam.Name}}}' is used more than once in route '{route}'.");
                return Task.FromResult(1);
            }
        }

        if (commandName is not null && !CSharpIdentifierValidator.IsValid(commandName))
        {
            console.WriteError($"'{commandName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (queryName is not null && !CSharpIdentifierValidator.IsValid(queryName))
        {
            console.WriteError($"'{queryName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (resultType is not null && !CSharpIdentifierValidator.IsValidTypeName(resultType))
        {
            console.WriteError($"'{resultType}' is not a valid C# type name.");
            return Task.FromResult(1);
        }

        if (commandName is not null && queryName is not null)
        {
            console.WriteError("Options --command and --query are mutually exclusive. Specify only one.");
            return Task.FromResult(1);
        }

        if (queryName is not null && resultType is null)
        {
            console.WriteError("Option --result-type is required when using --query.");
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

        var moduleDir = Path.Combine(solutionRoot, "src", "Modules", moduleName);
        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' was not found at '{moduleDir}'. Run 'modulus add-module {moduleName}' first.");
            return Task.FromResult(1);
        }

        var apiProjectDir = Path.Combine(moduleDir, "src", $"{moduleName}.Api");
        if (!fileSystem.DirectoryExists(apiProjectDir))
        {
            console.WriteError($"Module '{moduleName}' has no Api project at '{apiProjectDir}' (it was likely scaffolded with --no-endpoints). Endpoints cannot be added to a module without one.");
            return Task.FromResult(1);
        }

        var endpointsDir = Path.Combine(apiProjectDir, "Endpoints");
        var endpointFilePath = PathGuard.EnsureContained(
            solutionRoot,
            Path.GetRelativePath(solutionRoot, Path.Combine(endpointsDir, $"{endpointName}.cs")));

        if (fileSystem.FileExists(endpointFilePath))
        {
            console.WriteError($"An endpoint file '{endpointName}.cs' already exists at '{endpointFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new EndpointGenerator();
        var output = generator.Generate(new EndpointOptions
        {
            EndpointName = endpointName,
            ModuleName = moduleName,
            SolutionName = solutionName,
            HttpMethod = method.ToUpperInvariant(),
            Route = route,
            CommandName = commandName,
            QueryName = queryName,
            ResultType = resultType,
        });

        if (dryRun)
        {
            console.WriteLine("Dry run — no files were written. The following would happen:");
            console.WriteLine($"  create  {endpointFilePath}  -- {method.ToUpperInvariant()} {route} endpoint");

            if (routeParams.Count > 0 && (commandName is not null || queryName is not null))
            {
                var targetName = commandName ?? queryName;
                console.WriteLine($"  note    {targetName} must declare positional parameters ({string.Join(", ", routeParams.Select(p => $"{p.ClrType} {p.Name}"))}) matching the route, in order");
            }

            console.WriteLine("Re-run without --dry-run to apply.");
            return Task.FromResult(0);
        }

        fileSystem.CreateDirectory(endpointsDir);
        fileSystem.WriteAllText(endpointFilePath, output.Content);

        console.WriteSuccess($"Endpoint '{endpointName}' added to {moduleName} at Endpoints/{endpointName}.cs.");
        console.WriteLine($"  Method: {method.ToUpperInvariant()}");
        console.WriteLine($"  Route: /api/{moduleName.ToLowerInvariant()}{route}");
        if (commandName is not null) console.WriteLine($"  Wired to command: {commandName}");
        if (queryName is not null) console.WriteLine($"  Wired to query: {queryName}");

        if (routeParams.Count > 0)
        {
            var targetName = commandName ?? queryName;
            console.WriteLine($"  Route parameters: {string.Join(", ", routeParams.Select(p => $"{p.ClrType} {p.Name}"))}");

            if (targetName is not null)
            {
                console.WriteLine($"  Make sure {targetName} declares matching positional parameters, in this order, e.g.:");
                console.WriteLine($"    public sealed record {targetName}({string.Join(", ", routeParams.Select(p => $"{p.ClrType} {Capitalize(p.Name)}"))}, ...) : I...;");
            }
        }

        return Task.FromResult(0);
    }

    private static string Capitalize(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
