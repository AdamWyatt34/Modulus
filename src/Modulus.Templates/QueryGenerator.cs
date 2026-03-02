using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for CQRS query scaffolding.
/// Produces query record, handler, and unit test files.
/// </summary>
public sealed class QueryGenerator
{
    public IReadOnlyList<TemplateOutput> Generate(QueryOptions options)
    {
        return
        [
            GenerateQueryRecord(options),
            GenerateQueryHandler(options),
            GenerateUnitTest(options),
        ];
    }

    private static TemplateOutput GenerateQueryRecord(QueryOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Application.Queries.{o.QueryName}";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed record {o.QueryName} : IQuery<{o.ResultType}>;");

        var path = $"src/{o.ModuleName}.Application/Queries/{o.QueryName}/{o.QueryName}.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateQueryHandler(QueryOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Application.Queries.{o.QueryName}";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {o.QueryName}Handler : IQueryHandler<{o.QueryName}, {o.ResultType}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public Task<Result<{o.ResultType}>> Handle({o.QueryName} query, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: Implement query logic");
        sb.AppendLine("        throw new NotImplementedException();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Application/Queries/{o.QueryName}/{o.QueryName}Handler.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateUnitTest(QueryOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Tests.Unit.Queries";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine("using Shouldly;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Application.Queries.{o.QueryName};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public class {o.QueryName}HandlerTests");
        sb.AppendLine("{");
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Handle_should_return_success_with_value()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var handler = new {o.QueryName}Handler();");
        sb.AppendLine();
        sb.AppendLine("        // TODO: Implement test after handler logic is complete");
        sb.AppendLine("        await Should.ThrowAsync<NotImplementedException>(");
        sb.AppendLine($"            () => handler.Handle(new {o.QueryName}()));");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"tests/{o.ModuleName}.Tests.Unit/Queries/{o.QueryName}HandlerTests.cs";
        return new TemplateOutput(path, sb.ToString());
    }
}
