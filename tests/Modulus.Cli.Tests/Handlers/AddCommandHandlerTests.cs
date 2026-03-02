using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddCommandHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private AddCommandHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddCommandHandler(_fs, _console, solutionFinder);
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
    public async Task AddCommand_creates_command_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProduct.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddCommand_creates_handler()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProductHandler.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddCommand_creates_validator()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProductValidator.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddCommand_creates_unit_test()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Commands\CreateProductHandlerTests.cs").ShouldBeTrue();
    }

    // ── Void vs typed result ─────────────────────────────────────

    [Fact]
    public async Task AddCommand_void_uses_ICommand()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProduct.cs");
        content.ShouldContain(": ICommand;");
        content.ShouldNotContain("ICommand<");
    }

    [Fact]
    public async Task AddCommand_void_handler_returns_Result()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProductHandler.cs");
        content.ShouldContain("Task<Result> Handle");
        content.ShouldContain("Result.Success()");
    }

    [Fact]
    public async Task AddCommand_with_result_type_uses_ICommand_T()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", "Guid");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProduct.cs");
        content.ShouldContain(": ICommand<Guid>;");
    }

    [Fact]
    public async Task AddCommand_with_result_type_handler_returns_Result_T()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", "Guid");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProductHandler.cs");
        content.ShouldContain("Task<Result<Guid>> Handle");
        content.ShouldContain("throw new NotImplementedException()");
    }

    // ── Namespace and content correctness ────────────────────────

    [Fact]
    public async Task AddCommand_command_has_correct_namespace()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProduct.cs");
        content.ShouldContain("namespace EShop.Catalog.Application.Commands.CreateProduct;");
    }

    [Fact]
    public async Task AddCommand_validator_extends_AbstractValidator()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProductValidator.cs");
        content.ShouldContain("AbstractValidator<CreateProduct>");
    }

    [Fact]
    public async Task AddCommand_void_test_asserts_success()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Commands\CreateProductHandlerTests.cs");
        content.ShouldContain("Handle_should_return_success");
        content.ShouldContain("result.IsSuccess.ShouldBeTrue()");
    }

    [Fact]
    public async Task AddCommand_typed_test_asserts_not_implemented()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", "Guid");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Commands\CreateProductHandlerTests.cs");
        content.ShouldContain("Handle_should_return_success_with_value");
        content.ShouldContain("Should.ThrowAsync<NotImplementedException>");
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public async Task AddCommand_rejects_invalid_command_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddCommand_rejects_invalid_module_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "123Bad", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddCommand_rejects_nonexistent_module()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddCommand_rejects_duplicate_command()
    {
        SeedModulusSolutionWithModule();
        _fs.SeedFile(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Commands\CreateProduct\CreateProduct.cs", "existing");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddCommand_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Catalog", null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task AddCommand_rejects_invalid_result_type()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", "123Bad");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    // ── Success output ───────────────────────────────────────────

    [Fact]
    public async Task AddCommand_prints_success_message()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("CreateProduct", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("CreateProduct") && l.Contains("Catalog"));
    }
}
