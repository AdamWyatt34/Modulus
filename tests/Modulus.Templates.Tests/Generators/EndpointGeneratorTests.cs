using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class EndpointGeneratorTests
{
    private static EndpointOptions CreateOptions(
        string httpMethod = "GET",
        string route = "/{id:guid}",
        string? commandName = null,
        string? queryName = null,
        string? resultType = null) => new()
        {
            EndpointName = "GetProductById",
            ModuleName = "Catalog",
            SolutionName = "EShop",
            HttpMethod = httpMethod,
            Route = route,
            CommandName = commandName,
            QueryName = queryName,
            ResultType = resultType,
        };

    [Fact]
    public void Generate_ReturnsSingleOutputAtEndpointsPath()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions());

        output.RelativePath.ShouldBe("Endpoints/GetProductById.cs");
    }

    [Fact]
    public void Generate_ImplementsIEndpoint()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions());

        output.Content.ShouldContain("namespace EShop.Catalog.Api.Endpoints;");
        output.Content.ShouldContain("public sealed class GetProductById : IEndpoint");
        output.Content.ShouldContain("public void MapEndpoint(IEndpointRouteBuilder app)");
    }

    [Fact]
    public void Generate_QueryWired_UsesMediatorQueryAndMapGet()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(queryName: "GetProductById", resultType: "ProductDto"));

        output.Content.ShouldContain("app.MapGet(\"/{id:guid}\", async (IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Query(new GetProductById(), ct)");
        output.Content.ShouldContain(".Produces<ProductDto>(StatusCodes.Status200OK)");
    }

    [Fact]
    public void Generate_PostCommandWithResult_ReturnsCreated()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "POST",
            route: "",
            commandName: "CreateProduct",
            resultType: "Guid"));

        output.Content.ShouldContain("app.MapPost(\"\", async (IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Send(new CreateProduct(), ct)");
        output.Content.ShouldContain("Results.Created(");
        output.Content.ShouldContain(".Produces<Guid>(StatusCodes.Status201Created)");
    }

    [Fact]
    public void Generate_VoidCommand_ReturnsNoContent()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(httpMethod: "DELETE", commandName: "DeleteProduct"));

        output.Content.ShouldContain("app.MapDelete(");
        output.Content.ShouldContain("Results.NoContent");
        output.Content.ShouldContain(".Produces(StatusCodes.Status204NoContent)");
    }

    [Fact]
    public void Generate_NoCommandOrQuery_ProducesTodoStub()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions());

        output.Content.ShouldContain("// TODO: Wire up to a command or query");
        output.Content.ShouldContain("return Results.Ok();");
    }
}
