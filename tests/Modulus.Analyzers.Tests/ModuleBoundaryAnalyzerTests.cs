using Modulus.Analyzers.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Analyzers.Tests;

public class ModuleBoundaryAnalyzerTests
{
    private readonly ModuleBoundaryAnalyzer _analyzer = new();

    [Fact]
    public async Task ReferenceOtherModuleDomain_ReportsDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Catalog.Domain
            {
                public class Product
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }
            }
            """;

        const string source = """
            using Acme.Catalog.Domain;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void Process(Product product) { }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Catalog.Domain");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldNotBeEmpty();
        diagnostics.ShouldAllBe(d => d.Id == "MOD001");
        diagnostics.ShouldAllBe(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ReferenceOtherModuleIntegration_NoDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Catalog.Integration
            {
                public record ProductCreatedEvent(int ProductId, string Name);
            }
            """;

        const string source = """
            using Acme.Catalog.Integration;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void OnProductCreated(ProductCreatedEvent evt) { }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Catalog.Integration");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReferenceSameModule_NoDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Orders.Domain
            {
                public class Order
                {
                    public int Id { get; set; }
                }
            }
            """;

        const string source = """
            using Acme.Orders.Domain;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void Process(Order order) { }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Orders.Domain");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReferenceOtherModuleApplication_ReportsDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Catalog.Application
            {
                public class CatalogService
                {
                    public void DoWork() { }
                }
            }
            """;

        const string source = """
            using Acme.Catalog.Application;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void Process(CatalogService service) { }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Catalog.Application");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldNotBeEmpty();
        diagnostics.ShouldAllBe(d => d.Id == "MOD001");
    }

    [Fact]
    public async Task ReferenceOtherModuleApi_ReportsDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Catalog.Api
            {
                public class CatalogEndpoints
                {
                    public static void Map() { }
                }
            }
            """;

        const string source = """
            using Acme.Catalog.Api;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void Process() { CatalogEndpoints.Map(); }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Catalog.Api");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldNotBeEmpty();
        diagnostics.ShouldAllBe(d => d.Id == "MOD001");
    }

    [Fact]
    public async Task ReferenceBuildingBlocks_NoDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.BuildingBlocks.Domain
            {
                public abstract class Entity<TId>
                {
                    public TId Id { get; set; }
                }
            }
            """;

        const string source = """
            using Acme.BuildingBlocks.Domain;

            namespace Acme.Orders.Domain
            {
                public class Order : Entity<int> { }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Domain",
            referenceSource, "Acme.BuildingBlocks.Domain");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReferenceFrameworkType_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public List<string> GetNames() => new();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Application");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonModuleAssembly_NoDiagnostic()
    {
        const string source = """
            namespace SomeProject
            {
                public class Service
                {
                    public void Run() { }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "SomeProject");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReferenceOtherModuleInfrastructure_ReportsDiagnostic()
    {
        const string referenceSource = """
            namespace Acme.Catalog.Infrastructure
            {
                public class CatalogDbContext { }
            }
            """;

        const string source = """
            using Acme.Catalog.Infrastructure;

            namespace Acme.Orders.Application
            {
                public class OrderService
                {
                    public void UseDb(CatalogDbContext ctx) { }
                }
            }
            """;

        var compilation = AnalyzerTestHelper.CreateCompilationWithReference(
            source, "Acme.Orders.Application",
            referenceSource, "Acme.Catalog.Infrastructure");

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, compilation);

        diagnostics.ShouldNotBeEmpty();
        diagnostics.ShouldAllBe(d => d.Id == "MOD001");
    }

    [Fact]
    public void ParseModuleInfo_ValidAssemblyName_ReturnsCorrectInfo()
    {
        var info = ModuleBoundaryAnalyzer.ParseModuleInfo("Acme.Orders.Domain");

        info.ShouldNotBeNull();
        info.Prefix.ShouldBe("Acme");
        info.ModuleName.ShouldBe("Orders");
        info.Layer.ShouldBe("Domain");
    }

    [Fact]
    public void ParseModuleInfo_MultiSegmentPrefix_ReturnsCorrectInfo()
    {
        var info = ModuleBoundaryAnalyzer.ParseModuleInfo("Acme.Shop.Modules.Orders.Application");

        info.ShouldNotBeNull();
        info.Prefix.ShouldBe("Acme.Shop.Modules");
        info.ModuleName.ShouldBe("Orders");
        info.Layer.ShouldBe("Application");
    }

    [Fact]
    public void ParseModuleInfo_NoLayerSuffix_ReturnsNull()
    {
        var info = ModuleBoundaryAnalyzer.ParseModuleInfo("SomeProject");

        info.ShouldBeNull();
    }

    [Fact]
    public void ParseModuleInfo_TooShort_ReturnsNull()
    {
        var info = ModuleBoundaryAnalyzer.ParseModuleInfo("Domain");

        info.ShouldBeNull();
    }
}
