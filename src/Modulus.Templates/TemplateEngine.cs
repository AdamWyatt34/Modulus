using System.Reflection;
using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Reads embedded template resources, applies token replacement, and returns renderable file outputs.
/// </summary>
public sealed class TemplateEngine
{
    private static readonly Assembly ResourceAssembly = typeof(TemplateEngine).Assembly;

    /// <summary>
    /// Generates all files for a new solution scaffold.
    /// </summary>
    public IReadOnlyList<TemplateOutput> GenerateInit(InitOptions options)
    {
        var tokens = new Dictionary<string, string>
        {
            ["{{SolutionName}}"] = options.SolutionName,
            ["{{SolutionNameLower}}"] = options.SolutionName.ToLowerInvariant(),
            ["{{RootNamespace}}"] = options.SolutionName,
            ["{{ModulusKitVersion}}"] = options.ModulusKitVersion,
        };

        var outputs = new List<TemplateOutput>();

        foreach (var (resourceName, templatePath) in ResourceManifest.Entries)
        {
            if (!templatePath.StartsWith("init/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!options.IncludeAspire && templatePath.StartsWith("init/aspire/", StringComparison.Ordinal))
            {
                continue;
            }

            var content = ReadResource(resourceName);
            content = ReplaceTokens(content, tokens);

            var outputPath = templatePath["init/".Length..];
            outputPath = ReplaceTokens(outputPath, tokens);
            outputPath = StripTemplateExtension(outputPath);

            outputs.Add(new TemplateOutput(outputPath, content));
        }

        if (options.IncludeAspire)
        {
            InjectAspireIntoSlnx(outputs, options.SolutionName);
            InjectAspireIntoProgram(outputs, options.SolutionName);
            InjectAspireIntoWebApiCsproj(outputs, options.SolutionName);
        }

        return outputs;
    }

    /// <summary>
    /// Generates all files for a new module scaffold.
    /// </summary>
    public IReadOnlyList<TemplateOutput> GenerateModule(ModuleOptions options)
    {
        var tokens = new Dictionary<string, string>
        {
            ["{{SolutionName}}"] = options.SolutionName,
            ["{{SolutionNameLower}}"] = options.SolutionName.ToLowerInvariant(),
            ["{{RootNamespace}}"] = options.SolutionName,
            ["{{ModuleName}}"] = options.ModuleName,
            ["{{ModuleNameLower}}"] = options.ModuleName.ToLowerInvariant(),
        };

        var outputs = new List<TemplateOutput>();

        foreach (var (resourceName, templatePath) in ResourceManifest.Entries)
        {
            if (!templatePath.StartsWith("module/", StringComparison.Ordinal))
            {
                continue;
            }

            var content = ReadResource(resourceName);
            content = ReplaceTokens(content, tokens);

            var outputPath = templatePath["module/".Length..];
            outputPath = ReplaceTokens(outputPath, tokens);
            outputPath = StripTemplateExtension(outputPath);

            outputs.Add(new TemplateOutput(outputPath, content));
        }

        return outputs;
    }

    private static string ReadResource(string resourceName)
    {
        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReplaceTokens(string input, Dictionary<string, string> tokens)
    {
        foreach (var (token, value) in tokens)
        {
            input = input.Replace(token, value);
        }

        return input;
    }

    private static string StripTemplateExtension(string path)
    {
        if (path.EndsWith(".template", StringComparison.Ordinal))
        {
            return path[..^".template".Length];
        }

        return path;
    }

    private static void InjectAspireIntoProgram(List<TemplateOutput> outputs, string solutionName)
    {
        var programIndex = outputs.FindIndex(o =>
            o.RelativePath.EndsWith("Program.cs", StringComparison.Ordinal)
            && o.RelativePath.Contains($"{solutionName}.WebApi"));

        if (programIndex < 0)
        {
            return;
        }

        var program = outputs[programIndex];
        var content = program.Content;

        content = content.Replace(
            "var builder = WebApplication.CreateBuilder(args);",
            "var builder = WebApplication.CreateBuilder(args);\n\nbuilder.AddServiceDefaults();");

        content = content.Replace(
            "app.MapModuleEndpoints();",
            "app.MapDefaultEndpoints();\n\napp.MapModuleEndpoints();");

        outputs[programIndex] = program with { Content = content };
    }

    private static void InjectAspireIntoWebApiCsproj(List<TemplateOutput> outputs, string solutionName)
    {
        var csprojIndex = outputs.FindIndex(o =>
            o.RelativePath.EndsWith($"{solutionName}.WebApi.csproj", StringComparison.Ordinal));

        if (csprojIndex < 0)
        {
            return;
        }

        var csproj = outputs[csprojIndex];
        var serviceDefaultsRef =
            $"  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"..\\..\\aspire\\{solutionName}.ServiceDefaults\\{solutionName}.ServiceDefaults.csproj\" />\n" +
            $"  </ItemGroup>\n\n";

        var content = csproj.Content.Replace("</Project>", serviceDefaultsRef + "</Project>");
        outputs[csprojIndex] = csproj with { Content = content };
    }

    private static void InjectAspireIntoSlnx(List<TemplateOutput> outputs, string solutionName)
    {
        var slnxIndex = outputs.FindIndex(o => o.RelativePath.EndsWith(".slnx", StringComparison.Ordinal));
        if (slnxIndex < 0)
        {
            return;
        }

        var slnx = outputs[slnxIndex];
        var aspireEntries = $"""
          <Folder Name="/aspire/">
            <Project Path="aspire/{solutionName}.AppHost/{solutionName}.AppHost.csproj" />
            <Project Path="aspire/{solutionName}.ServiceDefaults/{solutionName}.ServiceDefaults.csproj" />
          </Folder>
        """;

        var content = slnx.Content.Replace("</Solution>", aspireEntries + "\n</Solution>");
        outputs[slnxIndex] = slnx with { Content = content };
    }
}
