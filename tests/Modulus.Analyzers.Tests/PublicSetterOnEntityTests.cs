using Modulus.Analyzers.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Analyzers.Tests;

public class PublicSetterOnEntityTests
{
    private readonly PublicSetterOnEntityAnalyzer _analyzer = new();

    [Fact]
    public async Task PublicSetter_OnEntitySubclass_ReportsDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD005");
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
    }

    [Fact]
    public async Task PublicSetter_OnAggregateRootSubclass_ReportsDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public abstract class AggregateRoot<TId> : Entity<TId> { }
            public class Order : AggregateRoot<int>
            {
                public string Name { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD005");
    }

    [Fact]
    public async Task PrivateSetter_OnEntity_NoDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; private set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProtectedSetter_OnEntity_NoDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; protected set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task InitAccessor_OnEntity_NoDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; init; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadOnlyProperty_OnEntity_NoDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublicSetter_OnNonEntity_NoDiagnostic()
    {
        const string source = """
            public class OrderDto
            {
                public string Name { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublicSetter_OnNestedEntityClass_ReportsDiagnostic()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Outer
            {
                public class Order : Entity<int>
                {
                    public string Name { get; set; }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD005");
    }

    [Fact]
    public async Task MultiplePublicSetters_OnEntity_ReportsMultipleDiagnostics()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; set; }
                public int Quantity { get; set; }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(2);
    }

    [Fact]
    public async Task CodeFix_ChangesToPrivateSet()
    {
        const string source = """
            public abstract class Entity<TId> { }
            public class Order : Entity<int>
            {
                public string Name { get; set; }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new PublicSetterOnEntityCodeFixProvider(),
            source,
            "MOD005");

        fixedSource.ShouldContain("private set");
        fixedSource.ShouldNotContain("{ get; set; }");
    }
}
