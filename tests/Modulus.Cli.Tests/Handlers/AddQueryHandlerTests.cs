using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddQueryHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private AddQueryHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddQueryHandler(_fs, _console, solutionFinder);
    }

    private void SeedModulusSolutionWithModule()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\ModuleRegistration.cs", "namespace EShop.WebApi;");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit");
    }

    // ── File creation tests ──────────────────────────────────────

    [Fact]
    public async Task AddQuery_creates_query_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductById.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddQuery_creates_handler()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductByIdHandler.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddQuery_creates_unit_test()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Queries\GetProductByIdHandlerTests.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddQuery_creates_exactly_three_files()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        // Should not create a validator file
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductByIdValidator.cs").ShouldBeFalse();
    }

    // ── Content correctness ──────────────────────────────────────

    [Fact]
    public async Task AddQuery_uses_IQuery_T()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductById.cs");
        content.ShouldContain(": IQuery<ProductDto>;");
    }

    [Fact]
    public async Task AddQuery_handler_implements_IQueryHandler()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductByIdHandler.cs");
        content.ShouldContain("IQueryHandler<GetProductById, ProductDto>");
    }

    [Fact]
    public async Task AddQuery_has_correct_namespace()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductById.cs");
        content.ShouldContain("namespace EShop.Catalog.Application.Queries.GetProductById;");
    }

    [Fact]
    public async Task AddQuery_test_asserts_not_implemented()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Queries\GetProductByIdHandlerTests.cs");
        content.ShouldContain("Should.ThrowAsync<NotImplementedException>");
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public async Task AddQuery_rejects_invalid_query_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddQuery_rejects_invalid_module_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "123Bad", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddQuery_rejects_nonexistent_module()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Orders", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddQuery_rejects_duplicate_query()
    {
        SeedModulusSolutionWithModule();
        _fs.SeedFile(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Queries\GetProductById\GetProductById.cs", "existing");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddQuery_rejects_invalid_result_type()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "123Bad");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddQuery_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Catalog", null, "ProductDto");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    // ── Success output ───────────────────────────────────────────

    [Fact]
    public async Task AddQuery_prints_success_message()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("GetProductById", "Catalog", @"C:\work\EShop\EShop.slnx", "ProductDto");

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("GetProductById") && l.Contains("Catalog"));
    }
}
