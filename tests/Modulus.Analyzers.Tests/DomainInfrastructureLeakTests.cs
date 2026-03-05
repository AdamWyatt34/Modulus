using Modulus.Analyzers.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Analyzers.Tests;

public class DomainInfrastructureLeakTests
{
    private readonly DomainInfrastructureLeakAnalyzer _analyzer = new();

    [Fact]
    public async Task TableAttribute_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task KeyAttribute_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;

            public class Order
            {
                [Key]
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task RequiredAttribute_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;

            public class Order
            {
                [Required]
                public string Name { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task JsonPropertyNameAttribute_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using System.Text.Json.Serialization;

            public class Order
            {
                [JsonPropertyName("name")]
                public string Name { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task EfCoreUsing_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task EfCoreSubNamespaceUsing_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore.Migrations;

            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task Attribute_InInfrastructureAssembly_NoDiagnostic()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Orders")]
            public class OrderConfiguration
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Infrastructure");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task CustomAttribute_InDomainAssembly_NoDiagnostic()
    {
        const string source = """
            public class MyCustomAttribute : System.Attribute { }

            [MyCustom]
            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task NewtonsoftUsing_InDomainAssembly_ReportsDiagnostic()
    {
        const string source = """
            using Newtonsoft.Json;

            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            _analyzer, source, "Acme.Orders.Domain");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD004");
    }

    [Fact]
    public async Task CodeFix_RemovesAttribute()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new DomainInfrastructureLeakCodeFixProvider(),
            source,
            "MOD004",
            "Acme.Orders.Domain");

        fixedSource.ShouldNotContain("[Table");
        fixedSource.ShouldContain("public class Order");
    }

    [Fact]
    public async Task CodeFix_RemovesUsingDirective()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new DomainInfrastructureLeakCodeFixProvider(),
            source,
            "MOD004",
            "Acme.Orders.Domain");

        fixedSource.ShouldNotContain("using Microsoft.EntityFrameworkCore");
        fixedSource.ShouldContain("public class Order");
    }
}
