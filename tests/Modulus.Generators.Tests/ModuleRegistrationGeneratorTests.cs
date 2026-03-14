using Microsoft.CodeAnalysis;
using Modulus.Generators.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Generators.Tests;

public class ModuleRegistrationGeneratorTests
{
    private const string SharedUsings = """
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        """;

    private const string IModuleRegistrationInterface = """
        namespace BuildingBlocks.Infrastructure.Registration
        {
            public interface IModuleRegistration
            {
                static abstract IServiceCollection ConfigureServices(
                    IServiceCollection services, IConfiguration configuration);
                static abstract IEndpointRouteBuilder ConfigureEndpoints(
                    IEndpointRouteBuilder endpoints);
            }
        }
        """;

    private const string ModuleOrderAttributeSource = """
        namespace Modulus.Mediator.Abstractions
        {
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class ModuleOrderAttribute : System.Attribute
            {
                public int Order { get; }
                public ModuleOrderAttribute(int order) => Order = order;
            }
        }
        """;

    private static string BuildModuleSource(params string[] namespaceBlocks)
    {
        return SharedUsings + "\nusing BuildingBlocks.Infrastructure.Registration;\n"
            + IModuleRegistrationInterface + "\n"
            + string.Join("\n", namespaceBlocks);
    }

    [Fact]
    public void Generate_SingleModule_RegistersBothMethods()
    {
        var moduleSource = BuildModuleSource("""
            namespace Orders.Infrastructure
            {
                public sealed class OrdersModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """);

        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", moduleSource);

        var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "GeneratedModuleRegistration.g.cs");

        generated.ShouldContain("Orders.Infrastructure.OrdersModule.ConfigureServices(services, configuration);");
        generated.ShouldContain("Orders.Infrastructure.OrdersModule.ConfigureEndpoints(app);");
        generated.ShouldContain("namespace TestHost;");
        generated.ShouldContain("public static class GeneratedModuleRegistration");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_MultipleModules_AllPresentAlphabetical()
    {
        var moduleSource = BuildModuleSource("""
            namespace Orders.Infrastructure
            {
                public sealed class OrdersModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """, """
            namespace Inventory.Infrastructure
            {
                public sealed class InventoryModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """);

        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", moduleSource);

        var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "GeneratedModuleRegistration.g.cs");

        // Both modules present
        generated.ShouldContain("Inventory.Infrastructure.InventoryModule.ConfigureServices(services, configuration);");
        generated.ShouldContain("Orders.Infrastructure.OrdersModule.ConfigureServices(services, configuration);");
        generated.ShouldContain("Inventory.Infrastructure.InventoryModule.ConfigureEndpoints(app);");
        generated.ShouldContain("Orders.Infrastructure.OrdersModule.ConfigureEndpoints(app);");

        // Inventory should come before Orders (alphabetical) in both sections
        var inventoryServicesIndex = generated.IndexOf("Inventory.Infrastructure.InventoryModule.ConfigureServices");
        var ordersServicesIndex = generated.IndexOf("Orders.Infrastructure.OrdersModule.ConfigureServices");
        inventoryServicesIndex.ShouldBeLessThan(ordersServicesIndex);

        var inventoryEndpointsIndex = generated.IndexOf("Inventory.Infrastructure.InventoryModule.ConfigureEndpoints");
        var ordersEndpointsIndex = generated.IndexOf("Orders.Infrastructure.OrdersModule.ConfigureEndpoints");
        inventoryEndpointsIndex.ShouldBeLessThan(ordersEndpointsIndex);

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_ModuleWithOrderAttribute_OrderedCorrectly()
    {
        var moduleSource = SharedUsings
            + "using BuildingBlocks.Infrastructure.Registration;\n"
            + "using Modulus.Mediator.Abstractions;\n"
            + IModuleRegistrationInterface + "\n"
            + ModuleOrderAttributeSource + "\n"
            + """
            namespace Payments.Infrastructure
            {
                [ModuleOrder(1)]
                public sealed class PaymentsModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }

            namespace Catalog.Infrastructure
            {
                public sealed class CatalogModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """;

        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", moduleSource);

        var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "GeneratedModuleRegistration.g.cs");

        // Payments (order=1) should come before Catalog (order=MaxValue)
        var paymentsIndex = generated.IndexOf("Payments.Infrastructure.PaymentsModule.ConfigureServices");
        var catalogIndex = generated.IndexOf("Catalog.Infrastructure.CatalogModule.ConfigureServices");
        paymentsIndex.ShouldBeLessThan(catalogIndex);

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_NoModules_GeneratesEmptyMethods()
    {
        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost");

        var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "GeneratedModuleRegistration.g.cs");

        generated.ShouldContain("public static class GeneratedModuleRegistration");
        generated.ShouldContain("public static IServiceCollection AddAllModules(");
        generated.ShouldContain("public static IEndpointRouteBuilder MapAllModuleEndpoints(");
        generated.ShouldContain("return services;");
        generated.ShouldContain("return app;");
        generated.ShouldNotContain("ConfigureServices(services");
        generated.ShouldNotContain("ConfigureEndpoints(app");
        generated.ShouldNotContain("Auto-discovered");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_IncompleteModule_EmitsMODGEN004AndSkips()
    {
        var moduleSource = BuildModuleSource("""
            namespace Broken.Infrastructure
            {
                public sealed class BrokenModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;

                    // Explicit interface implementation only — not a public static method on the type
                    static IEndpointRouteBuilder IModuleRegistration.ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """);

        var hostSource = "namespace TestHost { public class Marker { } }";

        var (_, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", moduleSource);

        var generated = GeneratorTestHelper.GetGeneratedSource(runResult, "GeneratedModuleRegistration.g.cs");

        // The incomplete module should not appear in the generated code
        generated.ShouldNotContain("BrokenModule");

        // MODGEN004 diagnostic should be emitted
        var diagnostics = runResult.Results
            .SelectMany(r => r.Diagnostics)
            .Where(d => d.Id == "MODGEN004")
            .ToList();
        diagnostics.Count.ShouldBeGreaterThan(0);
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generate_SingleModule_GeneratedCodeCompiles()
    {
        var moduleSource = BuildModuleSource("""
            namespace Orders.Infrastructure
            {
                public sealed class OrdersModule : IModuleRegistration
                {
                    public static IServiceCollection ConfigureServices(
                        IServiceCollection services, IConfiguration configuration)
                        => services;
                    public static IEndpointRouteBuilder ConfigureEndpoints(
                        IEndpointRouteBuilder endpoints)
                        => endpoints;
                }
            }
            """);

        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, _) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", moduleSource);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        errors.ShouldBeEmpty();

        // Verify we can emit the compilation to a valid assembly
        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        emitResult.Success.ShouldBeTrue();
    }

    [Fact]
    public void Generate_NonAspNetCoreProject_SkipsEmission()
    {
        var hostSource = "namespace TestHost { public class Marker { } }";

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunModuleRegistrationGenerator(
            hostSource, "TestHost", aspNetCoreReferences: false);

        runResult.GeneratedTrees
            .Any(t => t.FilePath.EndsWith("GeneratedModuleRegistration.g.cs"))
            .ShouldBeFalse();

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }
}
