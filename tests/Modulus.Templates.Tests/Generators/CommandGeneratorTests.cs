using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class CommandGeneratorTests
{
    private static CommandOptions CreateOptions(string? resultType = null) => new()
    {
        CommandName = "CreateProduct",
        ModuleName = "Catalog",
        SolutionName = "EShop",
        ResultType = resultType,
    };

    [Fact]
    public void Generate_VoidCommand_ProducesFourOutputs()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions());

        outputs.Count.ShouldBe(4);
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Commands/CreateProduct/CreateProduct.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Commands/CreateProduct/CreateProductHandler.cs");
        outputs.ShouldContain(o => o.RelativePath == "src/Catalog.Application/Commands/CreateProduct/CreateProductValidator.cs");
        outputs.ShouldContain(o => o.RelativePath == "tests/Catalog.Tests.Unit/Commands/CreateProductHandlerTests.cs");
    }

    [Fact]
    public void Generate_VoidCommand_RecordImplementsICommand()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions());

        var record = outputs.Single(o => o.RelativePath.EndsWith("CreateProduct.cs"));
        record.Content.ShouldContain("namespace EShop.Catalog.Application.Commands.CreateProduct;");
        record.Content.ShouldContain("public sealed record CreateProduct : ICommand;");
    }

    [Fact]
    public void Generate_VoidCommand_HandlerReturnsResult()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions());

        var handler = outputs.Single(o => o.RelativePath.EndsWith("CreateProductHandler.cs"));
        handler.Content.ShouldContain("public sealed class CreateProductHandler : ICommandHandler<CreateProduct>");
        handler.Content.ShouldContain("Task<Result> Handle(CreateProduct command, CancellationToken cancellationToken = default)");
        handler.Content.ShouldContain("Result.Success()");
    }

    [Fact]
    public void Generate_TypedCommand_RecordImplementsICommandOfT()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions("Guid"));

        var record = outputs.Single(o => o.RelativePath.EndsWith("CreateProduct.cs"));
        record.Content.ShouldContain("public sealed record CreateProduct : ICommand<Guid>;");
    }

    [Fact]
    public void Generate_TypedCommand_HandlerReturnsResultOfT()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions("Guid"));

        var handler = outputs.Single(o => o.RelativePath.EndsWith("CreateProductHandler.cs"));
        handler.Content.ShouldContain("public sealed class CreateProductHandler : ICommandHandler<CreateProduct, Guid>");
        handler.Content.ShouldContain("Task<Result<Guid>> Handle(CreateProduct command, CancellationToken cancellationToken = default)");
        handler.Content.ShouldContain("throw new NotImplementedException();");
    }

    [Fact]
    public void Generate_ValidatorExtendsAbstractValidator()
    {
        var generator = new CommandGenerator();

        var outputs = generator.Generate(CreateOptions());

        var validator = outputs.Single(o => o.RelativePath.EndsWith("CreateProductValidator.cs"));
        validator.Content.ShouldContain("public sealed class CreateProductValidator : AbstractValidator<CreateProduct>");
    }
}
