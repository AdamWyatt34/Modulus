using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddEndpointHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private FakeConsole _console = new();

    private AddEndpointHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddEndpointHandler(_fs, _console, solutionFinder);
    }

    private void SeedModulusSolutionWithModule()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints");
    }

    // ── Endpoint file creation tests ─────────────────────────────

    [Fact]
    public async Task AddEndpoint_creates_endpoint_file()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddEndpoint_file_contains_IEndpoint_class()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("class GetProducts : IEndpoint");
        content.ShouldContain("void MapEndpoint(IEndpointRouteBuilder app)");
    }

    [Fact]
    public async Task AddEndpoint_with_query_generates_mediator_query_call()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, "GetProductList", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("mediator.Query(new GetProductList()");
    }

    [Fact]
    public async Task AddEndpoint_with_command_generates_mediator_send_call()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", "CreateProduct", null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("mediator.Send(new CreateProduct()");
    }

    [Fact]
    public async Task AddEndpoint_with_query_adds_using_directive()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, "GetProductList", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("using EShop.Catalog.Application.Queries.GetProductList;");
    }

    [Fact]
    public async Task AddEndpoint_with_command_adds_using_directive()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", "CreateProduct", null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("using EShop.Catalog.Application.Commands.CreateProduct;");
    }

    [Fact]
    public async Task AddEndpoint_with_wiring_adds_extensions_using()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", "CreateProduct", null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("using EShop.WebApi.Extensions;");
    }

    [Fact]
    public async Task AddEndpoint_without_command_or_query_generates_bare_scaffold()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("// TODO: Wire up to a command or query");
    }

    [Fact]
    public async Task AddEndpoint_with_query_uses_Match_pattern()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, "GetProductList", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("result.Match(Results.Ok, ApiResults.Problem)");
    }

    // ── HTTP method mapping ──────────────────────────────────────

    [Fact]
    public async Task AddEndpoint_get_uses_MapGet()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs");
        content.ShouldContain("app.MapGet(");
    }

    [Fact]
    public async Task AddEndpoint_post_uses_MapPost()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("app.MapPost(");
    }

    [Fact]
    public async Task AddEndpoint_put_uses_MapPut()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("UpdateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "PUT", "/{id:guid}", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\UpdateProduct.cs");
        content.ShouldContain("app.MapPut(");
    }

    [Fact]
    public async Task AddEndpoint_delete_uses_MapDelete()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("DeleteProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "DELETE", "/{id:guid}", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\DeleteProduct.cs");
        content.ShouldContain("app.MapDelete(");
    }

    [Fact]
    public async Task AddEndpoint_sets_endpoint_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/{id:guid}", null, null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProductById.cs");
        content.ShouldContain(".WithName(\"GetProductById\")");
    }

    // ── Command result type variations ───────────────────────────

    [Fact]
    public async Task AddEndpoint_post_with_typed_command_uses_created()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", "CreateProduct", null, "Guid");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("Results.Created");
        content.ShouldContain("Status201Created");
    }

    [Fact]
    public async Task AddEndpoint_void_command_uses_no_content()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", "CreateProduct", null, null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs");
        content.ShouldContain("Status204NoContent");
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public async Task AddEndpoint_rejects_invalid_http_method()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "PATCH", "/", null, null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("PATCH"));
    }

    [Fact]
    public async Task AddEndpoint_rejects_mutually_exclusive_command_and_query()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", "SomeCommand", "SomeQuery", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("mutually exclusive"));
    }

    [Fact]
    public async Task AddEndpoint_rejects_query_without_result_type()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, "GetProductList", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("--result-type"));
    }

    [Fact]
    public async Task AddEndpoint_rejects_nonexistent_module()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Orders", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddEndpoint_rejects_duplicate_endpoint_file()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        // Add the first endpoint
        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        // Try to add a duplicate
        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/products", null, null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddEndpoint_allows_multiple_different_endpoints()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        // Add first endpoint
        await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        // Add second endpoint
        _console = new FakeConsole();
        var handler2 = new AddEndpointHandler(_fs, _console, new SolutionFinder(_fs));
        var result = await handler2.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx",
            "POST", "/", null, null, null);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetProducts.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CreateProduct.cs").ShouldBeTrue();
    }

    // ── Success output ───────────────────────────────────────────

    [Fact]
    public async Task AddEndpoint_prints_success_message()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProducts", "Catalog", @"C:\work\EShop\EShop.slnx",
            "GET", "/", null, null, null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("GetProducts"));
    }
}
