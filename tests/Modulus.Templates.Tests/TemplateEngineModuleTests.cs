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

    // ── H-PKG3 / C4: analyzer/generator references flow neither transitively across ─────
    // ── ProjectReference nor across the module boundary, so MOD rules and              ─────
    // ── [StronglyTypedId] were previously inert everywhere module code actually lives. ─────

    [Theory]
    [InlineData("src/Catalog.Domain/Catalog.Domain.csproj")]
    [InlineData("src/Catalog.Application/Catalog.Application.csproj")]
    [InlineData("src/Catalog.Infrastructure/Catalog.Infrastructure.csproj")]
    [InlineData("src/Catalog.Api/Catalog.Api.csproj")]
    public void GenerateModule_LayerProjects_ReferenceModulusKitAnalyzers(string relativePath)
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var csproj = outputs.Single(o => o.RelativePath == relativePath);
        csproj.Content.ShouldContain(
            "<PackageReference Include=\"ModulusKit.Analyzers\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />");
    }

    [Theory]
    [InlineData("src/Catalog.Domain/Catalog.Domain.csproj")]
    [InlineData("src/Catalog.Application/Catalog.Application.csproj")]
    public void GenerateModule_DomainAndApplication_ReferenceModulusKitGenerators(string relativePath)
    {
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var csproj = outputs.Single(o => o.RelativePath == relativePath);
        csproj.Content.ShouldContain(
            "<PackageReference Include=\"ModulusKit.Generators\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />");
    }

    [Theory]
    [InlineData("src/Catalog.Infrastructure/Catalog.Infrastructure.csproj")]
    [InlineData("src/Catalog.Api/Catalog.Api.csproj")]
    public void GenerateModule_InfrastructureAndApi_DoNotReferenceModulusKitGenerators(string relativePath)
    {
        // Generators emits [StronglyTypedId] support for Domain types and handler/validator
        // registration for Application types — Infrastructure/Api have no use for it.
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var csproj = outputs.Single(o => o.RelativePath == relativePath);
        csproj.Content.ShouldNotContain("ModulusKit.Generators");
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
    public void GenerateModule_WriteDbContext_AttachesAuditableEntityInterceptor()
    {
        // The interceptor was scaffolded (BuildingBlocks.Infrastructure) but never registered
        // anywhere, so IAuditable.CreatedAtUtc/UpdatedAtUtc silently never got set.
        var engine = new TemplateEngine();

        var outputs = engine.GenerateModule(CreateOptions());

        var module = outputs.Single(o => o.RelativePath == "src/Catalog.Infrastructure/CatalogModule.cs");
        module.Content.ShouldContain("using EShop.BuildingBlocks.Infrastructure.Persistence;");
        module.Content.ShouldContain("options.AddInterceptors(new AuditableEntityInterceptor());");
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
