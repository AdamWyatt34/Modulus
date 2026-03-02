using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddEntityHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private AddEntityHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddEntityHandler(_fs, _console, solutionFinder);
    }

    private void SeedModulusSolutionWithModule()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\ModuleRegistration.cs", "namespace EShop.WebApi;");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit");
    }

    // ── File creation tests ──────────────────────────────────────

    [Fact]
    public async Task AddEntity_creates_entity_class_in_domain()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddEntity_creates_repository_interface()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Repositories\IProductRepository.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddEntity_creates_ef_repository()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Repositories\ProductRepository.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddEntity_creates_entity_configuration()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Configurations\ProductConfiguration.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddEntity_creates_unit_test()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Domain\ProductTests.cs").ShouldBeTrue();
    }

    // ── Aggregate vs Entity ──────────────────────────────────────

    [Fact]
    public async Task AddEntity_default_uses_Entity_base_class()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain(": Entity<Guid>");
        content.ShouldNotContain("AggregateRoot");
    }

    [Fact]
    public async Task AddEntity_aggregate_uses_AggregateRoot_base_class()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain(": AggregateRoot<Guid>");
    }

    [Fact]
    public async Task AddEntity_aggregate_repository_extends_IRepository()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Repositories\IProductRepository.cs");
        content.ShouldContain(": IRepository<Product, Guid>");
    }

    [Fact]
    public async Task AddEntity_non_aggregate_repository_is_standalone()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Repositories\IProductRepository.cs");
        content.ShouldNotContain(": IRepository<");
        content.ShouldContain("GetByIdAsync");
        content.ShouldContain("AddAsync");
        content.ShouldContain("Update");
        content.ShouldContain("Remove");
    }

    [Fact]
    public async Task AddEntity_aggregate_ef_repository_extends_EfRepository()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Repositories\ProductRepository.cs");
        content.ShouldContain("EfRepository<Product, Guid>");
        content.ShouldContain("IProductRepository");
    }

    [Fact]
    public async Task AddEntity_non_aggregate_ef_repository_is_standalone()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Repositories\ProductRepository.cs");
        content.ShouldNotContain("EfRepository<");
        content.ShouldContain("IProductRepository");
        content.ShouldContain("context.Set<Product>()");
    }

    // ── ID types ─────────────────────────────────────────────────

    [Fact]
    public async Task AddEntity_with_guid_id_type()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain("Entity<Guid>");
    }

    [Fact]
    public async Task AddEntity_with_int_id_type()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "int", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain("Entity<int>");
    }

    [Fact]
    public async Task AddEntity_with_custom_strongly_typed_id_generates_id_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "ProductId", properties: null);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Identifiers\ProductId.cs").ShouldBeTrue();

        var idContent = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Identifiers\ProductId.cs");
        idContent.ShouldContain("StronglyTypedId<ProductId>");
        idContent.ShouldContain("public static ProductId New()");

        var entityContent = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        entityContent.ShouldContain("AggregateRoot<ProductId>");
        entityContent.ShouldContain("using EShop.Catalog.Domain.Identifiers;");
    }

    [Fact]
    public async Task AddEntity_with_custom_id_generates_ef_conversion()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "ProductId", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Configurations\ProductConfiguration.cs");
        content.ShouldContain("HasConversion");
        content.ShouldContain("new ProductId(value)");
    }

    // ── Properties ───────────────────────────────────────────────

    [Fact]
    public async Task AddEntity_with_properties_generates_property_declarations()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: "Name:string,Price:decimal,IsActive:bool");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain("public string Name { get; private set; }");
        content.ShouldContain("public decimal Price { get; private set; }");
        content.ShouldContain("public bool IsActive { get; private set; }");
    }

    [Fact]
    public async Task AddEntity_with_properties_generates_factory_method()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: "Name:string,Price:decimal");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain("public static Product Create(Guid id, string name, decimal price)");
    }

    [Fact]
    public async Task AddEntity_with_properties_generates_ef_configuration()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: "Name:string,Price:decimal");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Configurations\ProductConfiguration.cs");
        content.ShouldContain("builder.Property(e => e.Name)");
        content.ShouldContain("HasMaxLength(200)");
        content.ShouldContain("builder.Property(e => e.Price)");
    }

    [Fact]
    public async Task AddEntity_with_properties_generates_test_with_assertions()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: "Name:string");

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Domain\ProductTests.cs");
        content.ShouldContain("Create_should_set_properties");
        content.ShouldContain("entity.Name.ShouldBe(");
    }

    // ── Namespace correctness ────────────────────────────────────

    [Fact]
    public async Task AddEntity_entity_has_correct_namespace()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs");
        content.ShouldContain("namespace EShop.Catalog.Domain.Entities;");
    }

    [Fact]
    public async Task AddEntity_configuration_has_correct_namespace()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\Configurations\ProductConfiguration.cs");
        content.ShouldContain("namespace EShop.Catalog.Infrastructure.Persistence.Configurations;");
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public async Task AddEntity_rejects_invalid_entity_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddEntity_rejects_invalid_module_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "123Bad", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddEntity_rejects_nonexistent_module()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Orders", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddEntity_rejects_duplicate_entity()
    {
        SeedModulusSolutionWithModule();
        _fs.SeedFile(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Entities\Product.cs", "existing");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddEntity_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", null,
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task AddEntity_returns_error_when_not_modulus_solution()
    {
        _fs.SetCurrentDirectory(@"C:\work\Other");
        _fs.SeedFile(@"C:\work\Other\Other.slnx", "<Solution />");
        _fs.SeedDirectory(@"C:\work\Other\src\Modules\Catalog");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\Other\Other.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("does not appear to be a Modulus solution"));
    }

    [Fact]
    public async Task AddEntity_with_invalid_property_format_returns_error()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: "BadFormat");

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("BadFormat"));
    }

    // ── Success output ───────────────────────────────────────────

    [Fact]
    public async Task AddEntity_prints_success_message()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: true, idType: "guid", properties: null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("Product") && l.Contains("aggregate root"));
    }

    [Fact]
    public async Task AddEntity_prints_entity_type_for_non_aggregate()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Product", "Catalog", @"C:\work\EShop\EShop.slnx",
            isAggregate: false, idType: "guid", properties: null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("entity"));
    }
}
