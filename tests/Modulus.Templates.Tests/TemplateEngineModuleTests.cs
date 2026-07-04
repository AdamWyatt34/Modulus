using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests;

public class TemplateEngineModuleTests
{
    private static ModuleOptions CreateOptions() => new()
    {
        ModuleName = "Catalog",
        SolutionName = "EShop",
    };

    [Fact]
    public void GenerateModule_ProducesFiveSrcProjects()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Domain/Catalog.Domain.csproj");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Catalog.Application.csproj");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Infrastructure/Catalog.Infrastructure.csproj");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Integration/Catalog.Integration.csproj");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Api/Catalog.Api.csproj");
    }

    [Fact]
    public void GenerateModule_ProducesThreeTestProjects()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Unit/Catalog.Tests.Unit.csproj");
        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Integration/Catalog.Tests.Integration.csproj");
        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Architecture/Catalog.Tests.Architecture.csproj");
    }

    [Fact]
    public void GenerateModule_ModuleNameTokenReplacedInFileContent()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var module = outputs.Single(o => o.RelativePath == "src/Catalog.Infrastructure/CatalogModule.cs");
        module.Content.ShouldContain("namespace EShop.Catalog.Infrastructure;");
        module.Content.ShouldContain("public sealed class CatalogModule : IModuleRegistration");
        module.Content.ShouldContain("services.AddDbContext<CatalogDbContext>");
        module.Content.ShouldContain("endpoints.MapCatalogEndpoints();");
    }

    [Fact]
    public void GenerateModule_WriteDbContext_AttachesOutboxNotifyingInterceptor()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var module = outputs.Single(o => o.RelativePath == "src/Catalog.Infrastructure/CatalogModule.cs");
        module.Content.ShouldContain("using Modulus.Messaging.Outbox;");
        module.Content.ShouldContain("sp.GetService<OutboxNotifyingInterceptor>()");
        module.Content.ShouldContain("options.AddInterceptors(outboxInterceptor);");
    }

    [Fact]
    public void GenerateModule_ModuleNameTokenReplacedInFilePaths()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Infrastructure/Persistence/CatalogDbContext.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Infrastructure/Persistence/CatalogReadOnlyDbContext.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Api/Endpoints/CatalogEndpointRegistration.cs");
    }

    [Fact]
    public void GenerateModule_NoRemainingTokenPlaceholders()
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        foreach (var output in outputs)
        {
            output.Content.ShouldNotContain("{{", customMessage: $"Unresolved token found in {output.RelativePath}");
            output.RelativePath.ShouldNotContain("{{", customMessage: $"Unresolved token found in path {output.RelativePath}");
        }
    }
}
