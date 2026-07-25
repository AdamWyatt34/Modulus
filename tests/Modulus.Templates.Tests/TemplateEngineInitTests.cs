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
    public void GenerateInit_HostProgram_FiltersReadinessOnReadyTag()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        // The ModulusKit messaging health checks are tagged "ready"; /readyz must filter on
        // that tag (and the guidance block must show how to register them).
        var program = outputs.Single(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        program.Content.ShouldContain("check.Tags.Contains(\"ready\")");
        program.Content.ShouldContain("AddHealthChecks().AddModulusMessaging()");
    }

    [Fact]
    public void GenerateInit_HostProgram_ExplainsImmediateOutboxDispatch()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        var program = outputs.Single(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        program.Content.ShouldContain("New outbox rows are dispatched immediately");
        program.Content.ShouldContain("Messaging:OutboxPollInterval setting is only a fallback sweep");
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
    public void GenerateInit_DoesNotEmitUnregisteredIdempotentDomainEventHandler()
    {
        // Removed: it was scaffolded but never wired into any DI/decoration story, so it was
        // dead code in every scaffold. AuditableEntityInterceptor is the interceptor that *is*
        // now registered (see TemplateEngineModuleTests).
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        outputs.ShouldNotContain(o => o.RelativePath.Contains("IdempotentDomainEventHandler"));
    }

    [Fact]
    public void GenerateInit_DirectoryPackagesProps_PinsVulnerableMicrosoftOpenApiVersion()
    {
        // Mirrors samples/SampleApp/Directory.Packages.props: without this pin a fresh scaffold
        // pulls a transitive Microsoft.OpenApi version with a known advisory
        // (GHSA-v5pm-xwqc-g5wc) and fails the project's own vulnerable-package gate.
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions());

        var packages = outputs.Single(o => o.RelativePath == "Directory.Packages.props");
        packages.Content.ShouldContain("<PackageVersion Include=\"Microsoft.OpenApi\" Version=\"2.9.0\" />");
        packages.Content.ShouldContain("<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>");
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
    public void GenerateInit_IncludeAspireTrue_InjectsServiceDefaultsIntoHostProgram()
    {
        // Regression: the injection anchor used to be "app.MapModuleEndpoints();", which never
        // matches the host template's actual "app.MapAllModuleEndpoints();" line, so
        // `--aspire` silently never injected AddServiceDefaults()/MapDefaultEndpoints().
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true));

        var program = outputs.Single(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        program.Content.ShouldContain("builder.AddServiceDefaults();");
        program.Content.ShouldContain("app.MapDefaultEndpoints();");

        // MapDefaultEndpoints() must run before the module endpoints it complements.
        var defaultEndpointsIndex = program.Content.IndexOf("app.MapDefaultEndpoints();", StringComparison.Ordinal);
        var moduleEndpointsIndex = program.Content.IndexOf("app.MapAllModuleEndpoints();", StringComparison.Ordinal);
        defaultEndpointsIndex.ShouldBeGreaterThan(-1);
        moduleEndpointsIndex.ShouldBeGreaterThan(defaultEndpointsIndex);
    }

    [Fact]
    public void GenerateInit_IncludeAspireFalse_HostProgramHasNoAspireInjection()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: false));

        var program = outputs.Single(o => o.RelativePath == "src/EShop.WebApi/Program.cs");
        program.Content.ShouldNotContain("AddServiceDefaults");
        program.Content.ShouldNotContain("MapDefaultEndpoints");
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
        csproj.Content.ShouldContain("<PackageReference Include=\"Aspire.Hosting.RabbitMQ\" />");
    }

    [Fact]
    public void GenerateInit_Aspire_UsesCentrallyPinnedAspireVersion()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true, transport: "rabbitmq"));

        // Aspire.Hosting.Defaults does not exist on nuget.org — the AppHost must use the
        // Aspire.AppHost.Sdk MSBuild SDK + Aspire.Hosting.AppHost. The scaffold uses Central
        // Package Management, so csprojs must be versionless (NU1008) with pins in
        // Directory.Packages.props.
        var csproj = outputs.Single(o => o.RelativePath == "aspire/EShop.AppHost/EShop.AppHost.csproj");
        csproj.Content.ShouldContain(
            $"<Sdk Name=\"Aspire.AppHost.Sdk\" Version=\"{TemplateEngine.AspireVersion}\" />");
        csproj.Content.ShouldContain("<PackageReference Include=\"Aspire.Hosting.AppHost\" />");
        csproj.Content.ShouldNotContain("Aspire.Hosting.Defaults");
        csproj.Content.ShouldNotContain("PackageReference Include=\"Aspire.Hosting.AppHost\" Version=");

        var serviceDefaults = outputs.Single(
            o => o.RelativePath == "aspire/EShop.ServiceDefaults/EShop.ServiceDefaults.csproj");
        serviceDefaults.Content.ShouldNotContain("Aspire.Hosting.Defaults");
        serviceDefaults.Content.ShouldNotContain("Version=");

        var packages = outputs.Single(o => o.RelativePath == "Directory.Packages.props");
        packages.Content.ShouldContain(
            $"<PackageVersion Include=\"Aspire.Hosting.AppHost\" Version=\"{TemplateEngine.AspireVersion}\" />");
        packages.Content.ShouldContain(
            $"<PackageVersion Include=\"Aspire.Hosting.RabbitMQ\" Version=\"{TemplateEngine.AspireVersion}\" />");
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
    public void GenerateInit_AllXmlOutputs_AreWellFormed()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateInit(CreateOptions(includeAspire: true, transport: "rabbitmq"));

        // NuGet silently ignores a malformed Directory.Packages.props (CPM turns off and every
        // versionless PackageReference fails NU1015), so scaffolded XML must actually parse.
        // Classic trap: "--" is illegal inside an XML comment.
        foreach (var output in outputs.Where(o =>
            o.RelativePath.EndsWith(".csproj", StringComparison.Ordinal)
            || o.RelativePath.EndsWith(".props", StringComparison.Ordinal)
            || o.RelativePath.EndsWith(".targets", StringComparison.Ordinal)))
        {
            Should.NotThrow(
                () => System.Xml.Linq.XDocument.Parse(output.Content),
                $"Scaffolded XML is malformed: {output.RelativePath}");
        }
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
