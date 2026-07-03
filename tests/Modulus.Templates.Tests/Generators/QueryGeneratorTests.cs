using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class QueryGeneratorTests
{
    private static QueryOptions CreateOptions() => new()
    {
        QueryName = "GetProductById",
        ModuleName = "Catalog",
        SolutionName = "EShop",
        ResultType = "ProductDto",
    };

    [Fact]
    public void Generate_ProducesThreeOutputs()
    {
        var generator = new QueryGenerator();

        var outputs = generator.Generate(CreateOptions());

        outputs.Count.ShouldBe(3);
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Queries/GetProductById/GetProductById.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Queries/GetProductById/GetProductByIdHandler.cs");
        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Unit/Queries/GetProductByIdHandlerTests.cs");
    }

    [Fact]
    public void Generate_RecordImplementsIQueryOfT()
    {
        var generator = new QueryGenerator();

        var outputs = generator.Generate(CreateOptions());

        var record = outputs.Single(o => o.RelativePath.EndsWith("GetProductById.cs"));
        record.Content.ShouldContain("namespace EShop.Catalog.Application.Queries.GetProductById;");
        record.Content.ShouldContain("public sealed record GetProductById : IQuery<ProductDto>;");
    }

    [Fact]
    public void Generate_HandlerReturnsResultOfT()
    {
        var generator = new QueryGenerator();

        var outputs = generator.Generate(CreateOptions());

        var handler = outputs.Single(o => o.RelativePath.EndsWith("GetProductByIdHandler.cs"));
        handler.Content.ShouldContain("public sealed class GetProductByIdHandler : IQueryHandler<GetProductById, ProductDto>");
        handler.Content.ShouldContain("Task<Result<ProductDto>> Handle(GetProductById query, CancellationToken cancellationToken = default)");
    }

    [Fact]
    public void Generate_UnitTest_AssertsNotImplemented()
    {
        var generator = new QueryGenerator();

        var outputs = generator.Generate(CreateOptions());

        var test = outputs.Single(o => o.RelativePath.EndsWith("GetProductByIdHandlerTests.cs"));
        test.Content.ShouldContain("namespace EShop.Catalog.Tests.Unit.Queries;");
        test.Content.ShouldContain("Should.ThrowAsync<NotImplementedException>");
    }
}
