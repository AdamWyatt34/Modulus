using System.Collections.Generic;
using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class EntityGeneratorTests
{
    private static EntityOptions CreateOptions(
        bool isAggregate = false,
        string idType = "Guid",
        IReadOnlyList<EntityProperty>? properties = null) => new()
        {
            EntityName = "Product",
            ModuleName = "Catalog",
            SolutionName = "EShop",
            IsAggregate = isAggregate,
            IdType = idType,
            Properties = properties ?? [],
        };

    [Fact]
    public void Generate_DefaultOptions_ProducesFiveOutputs()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions());

        outputs.Count.ShouldBe(5);
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Domain/Entities/Product.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Domain/Repositories/IProductRepository.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Infrastructure/Persistence/Repositories/ProductRepository.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Infrastructure/Persistence/Configurations/ProductConfiguration.cs");
        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Unit/Domain/ProductTests.cs");
    }

    [Fact]
    public void Generate_NotAggregate_UsesEntityBaseClass()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions());

        var entity = outputs.Single(o => o.RelativePath.EndsWith("Entities/Product.cs"));
        entity.Content.ShouldContain("public class Product : Entity<Guid>");
    }

    [Fact]
    public void Generate_IsAggregate_UsesAggregateRootBaseClass()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions(isAggregate: true));

        var entity = outputs.Single(o => o.RelativePath.EndsWith("Entities/Product.cs"));
        entity.Content.ShouldContain("public class Product : AggregateRoot<Guid>");

        var repository = outputs.Single(o => o.RelativePath.EndsWith("IProductRepository.cs"));
        // Self-contained: no IRepository<,> base — Domain must not reference BuildingBlocks.Application.
        repository.Content.ShouldContain("public interface IProductRepository");
        repository.Content.ShouldNotContain("IRepository<Product, Guid>");
        repository.Content.ShouldNotContain("BuildingBlocks.Application");
        repository.Content.ShouldContain("Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);");
    }

    [Fact]
    public void Generate_UnitTest_DeclaresIdInEveryTestMethod()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions(isAggregate: true));

        var test = outputs.Single(o => o.RelativePath.EndsWith("ProductTests.cs"));
        // Both generated test methods pass `id` to Create and must declare it.
        var declarations = test.Content.Split("var id =").Length - 1;
        var usages = test.Content.Split(".Create(id").Length - 1;
        declarations.ShouldBe(usages);
    }

    [Fact]
    public void Generate_WithProperties_AddsPropertiesToEntityAndFactory()
    {
        var generator = new EntityGenerator();
        var properties = new List<EntityProperty> { new("Name", "string"), new("Price", "decimal") };

        var outputs = generator.Generate(CreateOptions(properties: properties));

        var entity = outputs.Single(o => o.RelativePath.EndsWith("Entities/Product.cs"));
        entity.Content.ShouldContain("public string Name { get; private set; } = default!;");
        entity.Content.ShouldContain("public decimal Price { get; private set; } = default!;");
        entity.Content.ShouldContain("public static Product Create(Guid id, string name, decimal price)");
    }

    [Fact]
    public void Generate_UnitTest_HoistsPropertyDefaultsIntoLocalsSharedByCreateAndAssertion()
    {
        // H-CLI4 regression: a non-deterministic default (Guid.NewGuid(), DateTime.UtcNow) must
        // be evaluated exactly once per test and reused for both the Create() argument and the
        // ShouldBe() assertion — otherwise the assertion compares two independently generated
        // values and the generated test permanently fails (Guid) or is flaky (DateTime).
        var generator = new EntityGenerator();
        var properties = new List<EntityProperty> { new("CustomerId", "Guid"), new("PlacedOn", "DateTime") };

        // idType is deliberately non-Guid here: the default id expression is also
        // "Guid.NewGuid()", which would otherwise inflate the CustomerId property's own
        // occurrence count to two (once for `id`, once for `customerId`) and invalidate the
        // "evaluated exactly once" assertion below without that meaning anything was wrong.
        var outputs = generator.Generate(CreateOptions(idType: "int", properties: properties));

        var test = outputs.Single(o => o.RelativePath.EndsWith("ProductTests.cs"));
        var propertiesTestStart = test.Content.IndexOf("Create_should_set_properties", StringComparison.Ordinal);
        propertiesTestStart.ShouldBeGreaterThan(-1);
        var propertiesTestBody = test.Content[propertiesTestStart..];

        propertiesTestBody.ShouldContain("var customerId = Guid.NewGuid();");
        propertiesTestBody.ShouldContain("var placedOn = DateTime.UtcNow;");
        propertiesTestBody.ShouldContain("Create(id, customerId, placedOn)");
        propertiesTestBody.ShouldContain("entity.CustomerId.ShouldBe(customerId);");
        propertiesTestBody.ShouldContain("entity.PlacedOn.ShouldBe(placedOn);");

        // Each non-deterministic expression must appear exactly once — as the local's
        // initializer — never a second time as a separately-evaluated assertion argument.
        CountOccurrences(propertiesTestBody, "Guid.NewGuid()").ShouldBe(1);
        CountOccurrences(propertiesTestBody, "DateTime.UtcNow").ShouldBe(1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void Generate_CustomStronglyTypedId_AddsIdentifierOutput()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions(idType: "ProductId"));

        outputs.Count.ShouldBe(6);
        var idFile = outputs.Single(o => o.RelativePath == "src/Catalog.Domain/Identifiers/ProductId.cs");
        idFile.Content.ShouldContain("public sealed record ProductId(Guid Value) : StronglyTypedId<ProductId>(Value)");
    }

    [Fact]
    public void Generate_BuiltInIdType_DoesNotAddIdentifierOutput()
    {
        var generator = new EntityGenerator();

        var outputs = generator.Generate(CreateOptions(idType: "int"));

        outputs.ShouldNotContain(o => o.RelativePath.Contains("Identifiers"));
    }
}
