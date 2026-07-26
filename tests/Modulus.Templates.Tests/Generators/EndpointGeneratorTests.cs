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

        // Default route is "/{id:guid}" — the route parameter is now bound as a leading lambda
        // parameter and forwarded positionally into the query's constructor.
        output.Content.ShouldContain("app.MapGet(\"/{id:guid}\", async (Guid id, IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Query(new GetProductById(id), ct)");
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
    public void Generate_PostCommandWithResult_DoesNotUseWebApiExtensionsUsing()
    {
        // H-CLI2: the module's Api project cannot reference the host WebApi project, and nothing
        // generated here needs that using anyway.
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "POST",
            route: "",
            commandName: "CreateProduct",
            resultType: "Guid"));

        output.Content.ShouldNotContain("WebApi.Extensions");
    }

    [Fact]
    public void Generate_PostCommandWithResult_RouteParam_BindsLambdaParameterAndInterpolatesLocation()
    {
        // Route parameters are now bound: "{itemId}" (no constraint) defaults to `string itemId`,
        // forwarded positionally into the command's constructor, and the Location value
        // interpolates the *actual* bound value rather than echoing the raw route template.
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "POST",
            route: "/items/{itemId}",
            commandName: "CreateProduct",
            resultType: "Guid"));

        output.Content.ShouldContain("async (string itemId, IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Send(new CreateProduct(itemId), ct)");
        output.Content.ShouldContain("Results.Created($\"/api/catalog/items/{itemId}\", value)");
    }

    [Fact]
    public void Generate_PostCommandWithResult_RouteParam_GuidConstraint_StripsConstraintFromLocationInterpolation()
    {
        // H-CLI2 regression, now resolved rather than avoided: a route containing "{id:guid}"
        // must never land inside a generated C# interpolated string verbatim — ":guid" would be
        // parsed as an interpolation alignment/format-string clause and throw a FormatException
        // at runtime (Guid has no "guid" format). The bound value must be interpolated via the
        // bare "{id}" hole instead.
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "POST",
            route: "/{id:guid}",
            commandName: "UpdateProduct",
            resultType: "Guid"));

        output.Content.ShouldContain("async (Guid id, IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Send(new UpdateProduct(id), ct)");
        output.Content.ShouldContain("Results.Created($\"/api/catalog/{id}\", value)");

        // The route template itself (the first MapPost argument) legitimately keeps its
        // constraint — only the Location interpolation must strip it.
        output.Content.ShouldNotContain("Results.Created($\"/api/catalog/{id:guid}\"");
    }

    [Fact]
    public void Generate_RouteParam_WithIntConstraint_BindsAsInt()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "GET",
            route: "/{page:int}",
            queryName: "ListProducts",
            resultType: "ProductListDto"));

        output.Content.ShouldContain("async (int page, IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Query(new ListProducts(page), ct)");
    }

    [Fact]
    public void Generate_RouteParam_WithOptionalIntConstraint_BindsAsNullableInt()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "GET",
            route: "/{page:int?}",
            queryName: "ListProducts",
            resultType: "ProductListDto"));

        output.Content.ShouldContain("async (int? page, IMediator mediator, CancellationToken ct) =>");
    }

    [Fact]
    public void Generate_MultipleRouteParams_BindInDeclaredOrder()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "GET",
            route: "/{parentId:guid}/items/{itemId:guid}",
            queryName: "GetItem",
            resultType: "ItemDto"));

        output.Content.ShouldContain("async (Guid parentId, Guid itemId, IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Query(new GetItem(parentId, itemId), ct)");
    }

    [Fact]
    public void Generate_NoCommandOrQuery_RouteParam_StillBindsLambdaParameter()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "DELETE",
            route: "/{id:guid}"));

        output.Content.ShouldContain("async (Guid id, CancellationToken ct) =>");
        output.Content.ShouldContain("// TODO: Wire up to a command or query");
    }

    [Fact]
    public void Generate_NoRouteParams_ConstructsParameterlessAsBefore()
    {
        var generator = new EndpointGenerator();

        var output = generator.Generate(CreateOptions(
            httpMethod: "POST",
            route: "",
            commandName: "CreateProduct",
            resultType: "Guid"));

        output.Content.ShouldContain("async (IMediator mediator, CancellationToken ct) =>");
        output.Content.ShouldContain("mediator.Send(new CreateProduct(), ct)");
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
