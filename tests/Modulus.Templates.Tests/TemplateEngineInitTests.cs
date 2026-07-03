using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests;

public class TemplateEngineInitTests
{
    private static InitOptions CreateOptions(
        bool includeAspire = false,
        string transport = "inmemory",
        string modulusKitVersion = "1.2.3") => new()
        {
            SolutionName = "EShop",
            IncludeAspire = includeAspire,
            Transport = transport,
            ModulusKitVersion = modulusKitVersion,
        };

    [Fact]
    public void GenerateInit_HostProgram_ImportsGeneratedExtensionsNamespace()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        // AddModulusHandlers/AddAllModules/MapAllModuleEndpoints are source-generated into the
        // host root namespace; top-level statements cannot see them without this using.
        var program = outputs.Single(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        program.Content.ShouldContain("using EShop.WebApi;");
    }

    [Fact]
    public void GenerateInit_Default_ProducesKeyFiles()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        outputs.ShouldContain(o => o.RelativePath == "Directory.Build.props");
        outputs.ShouldContain(o => o.RelativePath == "Directory.Packages.props");
        outputs.ShouldContain(o => o.RelativePath == "EShop.slnx");
        outputs.ShouldContain(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/EShop.WebApi/appsettings.json");
    }

    [Fact]
    public void GenerateInit_SubstitutesModulusKitVersionInDirectoryPackagesProps()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(modulusKitVersion: "9.9.9"));

        var packages = outputs.Single(o => o.RelativePath == "Directory.Packages.props");
        packages.Content.ShouldContain("<PackageVersion Include=\"ModulusKit.Mediator\" Version=\"9.9.9\" />");
        packages.Content.ShouldContain("<PackageVersion Include=\"ModulusKit.Mediator.Abstractions\" Version=\"9.9.9\" />");
        packages.Content.ShouldContain("<PackageVersion Include=\"ModulusKit.Messaging\" Version=\"9.9.9\" />");
        packages.Content.ShouldContain("<PackageVersion Include=\"ModulusKit.Analyzers\" Version=\"9.9.9\" />");
        packages.Content.ShouldContain("<PackageVersion Include=\"ModulusKit.Generators\" Version=\"9.9.9\" />");
    }

    [Fact]
    public void GenerateInit_IncludeAspireTrue_AddsAspireOutputsAndInjectsIntoSlnx()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true));

        outputs.ShouldContain(o => o.RelativePath == "aspire/EShop.AppHost/EShop.AppHost.csproj");
        outputs.ShouldContain(o => o.RelativePath == "aspire/EShop.AppHost/Program.cs");
        outputs.ShouldContain(o => o.RelativePath == "aspire/EShop.ServiceDefaults/EShop.ServiceDefaults.csproj");

        var slnx = outputs.Single(o => o.RelativePath == "EShop.slnx");
        slnx.Content.ShouldContain("EShop.AppHost/EShop.AppHost.csproj");
        slnx.Content.ShouldContain("EShop.ServiceDefaults/EShop.ServiceDefaults.csproj");
    }

    [Fact]
    public void GenerateInit_IncludeAspireFalse_YieldsNoAspirePaths()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: false));

        outputs.ShouldNotContain(o => o.RelativePath.StartsWith("aspire/"));
    }

    [Fact]
    public void GenerateInit_AspireWithRabbitMqTransport_InjectsRabbitMqIntoAppHost()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true, transport: "rabbitmq"));

        var program = outputs.Single(o => o.RelativePath == "aspire/EShop.AppHost/Program.cs");
        program.Content.ShouldContain("builder.AddRabbitMQ(\"messaging\")");

        var csproj = outputs.Single(o => o.RelativePath == "aspire/EShop.AppHost/EShop.AppHost.csproj");
        csproj.Content.ShouldContain("Aspire.Hosting.RabbitMQ");
    }

    [Fact]
    public void GenerateInit_AspireWithInMemoryTransport_DoesNotInjectRabbitMq()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true, transport: "inmemory"));

        var program = outputs.Single(o => o.RelativePath == "aspire/EShop.AppHost/Program.cs");
        program.Content.ShouldNotContain("AddRabbitMQ");

        var csproj = outputs.Single(o => o.RelativePath == "aspire/EShop.AppHost/EShop.AppHost.csproj");
        csproj.Content.ShouldNotContain("Aspire.Hosting.RabbitMQ");
    }

    [Fact]
    public void GenerateInit_NoRemainingTokenPlaceholders()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true, transport: "rabbitmq"));

        foreach (var output in outputs)
        {
            output.Content.ShouldNotContain("{{", customMessage: $"Unresolved token found in {output.RelativePath}");
        }
    }
}
