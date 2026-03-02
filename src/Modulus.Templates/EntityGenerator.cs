using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for entity scaffolding.
/// Produces the same <see cref="TemplateOutput"/> format as <see cref="TemplateEngine"/>
/// but handles dynamic property generation that token-based templates cannot.
/// </summary>
public sealed class EntityGenerator
{
    private static readonly HashSet<string> BuiltInIdTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "guid", "int", "long", "string",
    };

    public IReadOnlyList<TemplateOutput> Generate(EntityOptions options)
    {
        var outputs = new List<TemplateOutput>
        {
            GenerateEntityClass(options),
            GenerateRepositoryInterface(options),
            GenerateEfRepository(options),
            GenerateEntityConfiguration(options),
            GenerateUnitTest(options),
        };

        if (IsCustomStronglyTypedId(options.IdType))
        {
            outputs.Add(GenerateStronglyTypedId(options));
        }

        return outputs;
    }

    private static bool IsCustomStronglyTypedId(string idType) =>
        !BuiltInIdTypes.Contains(idType);

    private static TemplateOutput GenerateEntityClass(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Domain.Entities";
        var baseClass = o.IsAggregate ? "AggregateRoot" : "Entity";

        sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Domain.Entities;");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Identifiers;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public class {o.EntityName} : {baseClass}<{o.IdType}>");
        sb.AppendLine("{");

        foreach (var prop in o.Properties)
        {
            sb.AppendLine($"    public {prop.Type} {prop.Name} {{ get; private set; }} = default!;");
        }

        if (o.Properties.Count > 0)
        {
            sb.AppendLine();
        }

        sb.AppendLine($"    private {o.EntityName}() {{ }}");
        sb.AppendLine();

        // Factory method
        var factoryParams = new List<string> { $"{o.IdType} id" };
        foreach (var prop in o.Properties)
        {
            factoryParams.Add($"{prop.Type} {CamelCase(prop.Name)}");
        }

        sb.AppendLine($"    public static {o.EntityName} Create({string.Join(", ", factoryParams)})");
        sb.AppendLine("    {");
        sb.Append($"        return new {o.EntityName} {{ Id = id");

        foreach (var prop in o.Properties)
        {
            sb.Append($", {prop.Name} = {CamelCase(prop.Name)}");
        }

        sb.AppendLine(" };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Domain/Entities/{o.EntityName}.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateRepositoryInterface(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Domain.Repositories";

        if (o.IsAggregate)
        {
            sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Application.Persistence;");
        }

        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Entities;");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Identifiers;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        if (o.IsAggregate)
        {
            sb.AppendLine($"public interface I{o.EntityName}Repository : IRepository<{o.EntityName}, {o.IdType}>");
            sb.AppendLine("{");
            sb.AppendLine("}");
        }
        else
        {
            sb.AppendLine($"public interface I{o.EntityName}Repository");
            sb.AppendLine("{");
            sb.AppendLine($"    Task<{o.EntityName}?> GetByIdAsync({o.IdType} id, CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task<IReadOnlyList<{o.EntityName}>> ListAllAsync(CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task AddAsync({o.EntityName} entity, CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    void Update({o.EntityName} entity);");
            sb.AppendLine();
            sb.AppendLine($"    void Remove({o.EntityName} entity);");
            sb.AppendLine("}");
        }

        var path = $"src/{o.ModuleName}.Domain/Repositories/I{o.EntityName}Repository.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateEfRepository(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Infrastructure.Persistence.Repositories";

        sb.AppendLine("using Microsoft.EntityFrameworkCore;");

        if (o.IsAggregate)
        {
            sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Infrastructure.Persistence;");
        }

        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Entities;");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Identifiers;");
        }

        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Repositories;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        if (o.IsAggregate)
        {
            sb.AppendLine($"public class {o.EntityName}Repository(");
            sb.AppendLine($"    {o.ModuleName}DbContext context) : EfRepository<{o.EntityName}, {o.IdType}>(context), I{o.EntityName}Repository");
            sb.AppendLine("{");
            sb.AppendLine("}");
        }
        else
        {
            sb.AppendLine($"public class {o.EntityName}Repository(");
            sb.AppendLine($"    {o.ModuleName}DbContext context) : I{o.EntityName}Repository");
            sb.AppendLine("{");
            sb.AppendLine($"    public async Task<{o.EntityName}?> GetByIdAsync({o.IdType} id, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return await context.Set<{o.EntityName}>().FindAsync([id], cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public async Task<IReadOnlyList<{o.EntityName}>> ListAllAsync(CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return await context.Set<{o.EntityName}>().ToListAsync(cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public async Task AddAsync({o.EntityName} entity, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        await context.Set<{o.EntityName}>().AddAsync(entity, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public void Update({o.EntityName} entity) => context.Set<{o.EntityName}>().Update(entity);");
            sb.AppendLine();
            sb.AppendLine($"    public void Remove({o.EntityName} entity) => context.Set<{o.EntityName}>().Remove(entity);");
            sb.AppendLine("}");
        }

        var path = $"src/{o.ModuleName}.Infrastructure/Persistence/Repositories/{o.EntityName}Repository.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateEntityConfiguration(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Infrastructure.Persistence.Configurations";

        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Entities;");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Identifiers;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public class {o.EntityName}Configuration : IEntityTypeConfiguration<{o.EntityName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public void Configure(EntityTypeBuilder<{o.EntityName}> builder)");
        sb.AppendLine("    {");
        sb.AppendLine("        builder.HasKey(e => e.Id);");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine();
            sb.AppendLine($"        builder.Property(e => e.Id)");
            sb.AppendLine($"            .HasConversion(id => id.Value, value => new {o.IdType}(value));");
        }

        foreach (var prop in o.Properties)
        {
            sb.AppendLine();
            sb.Append($"        builder.Property(e => e.{prop.Name})");

            if (prop.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("            .IsRequired()");
                sb.AppendLine("            .HasMaxLength(200);");
            }
            else
            {
                sb.AppendLine(".IsRequired();");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Infrastructure/Persistence/Configurations/{o.EntityName}Configuration.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateUnitTest(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Tests.Unit.Domain";

        sb.AppendLine("using Shouldly;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Entities;");

        if (IsCustomStronglyTypedId(o.IdType))
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Domain.Identifiers;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public class {o.EntityName}Tests");
        sb.AppendLine("{");
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Create_should_set_id()");
        sb.AppendLine("    {");

        // Build the id value expression
        var idExpr = GetDefaultIdExpression(o.IdType);

        // Build factory call
        var args = new List<string> { "id" };
        foreach (var prop in o.Properties)
        {
            args.Add(GetDefaultValueExpression(prop.Type));
        }

        sb.AppendLine($"        var id = {idExpr};");
        sb.AppendLine();
        sb.AppendLine($"        var entity = {o.EntityName}.Create({string.Join(", ", args)});");
        sb.AppendLine();
        sb.AppendLine("        entity.Id.ShouldBe(id);");
        sb.AppendLine("    }");

        if (o.Properties.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    [Fact]");
            sb.AppendLine("    public void Create_should_set_properties()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var entity = {o.EntityName}.Create({string.Join(", ", args)});");
            sb.AppendLine();

            foreach (var prop in o.Properties)
            {
                var expected = GetDefaultValueExpression(prop.Type);
                sb.AppendLine($"        entity.{prop.Name}.ShouldBe({expected});");
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        var path = $"tests/{o.ModuleName}.Tests.Unit/Domain/{o.EntityName}Tests.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateStronglyTypedId(EntityOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Domain.Identifiers";

        sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Domain.Identifiers;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed record {o.IdType}(Guid Value) : StronglyTypedId<{o.IdType}>(Value)");
        sb.AppendLine("{");
        sb.AppendLine($"    public static {o.IdType} New() => new(Guid.NewGuid());");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Domain/Identifiers/{o.IdType}.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static string GetDefaultIdExpression(string idType) => idType.ToLowerInvariant() switch
    {
        "guid" => "Guid.NewGuid()",
        "int" => "1",
        "long" => "1L",
        "string" => "\"test-id\"",
        _ => $"{idType}.New()", // StronglyTypedId
    };

    private static string GetDefaultValueExpression(string type) => type.ToLowerInvariant() switch
    {
        "string" => "\"test\"",
        "int" => "1",
        "long" => "1L",
        "bool" => "true",
        "decimal" => "1.0m",
        "double" => "1.0",
        "float" => "1.0f",
        "datetime" => "DateTime.UtcNow",
        "guid" => "Guid.NewGuid()",
        _ => $"default({type})",
    };

    private static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
