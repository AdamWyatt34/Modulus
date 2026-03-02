using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for CQRS command scaffolding.
/// Produces command record, handler, validator, and unit test files.
/// </summary>
public sealed class CommandGenerator
{
    public IReadOnlyList<TemplateOutput> Generate(CommandOptions options)
    {
        return
        [
            GenerateCommandRecord(options),
            GenerateCommandHandler(options),
            GenerateValidator(options),
            GenerateUnitTest(options),
        ];
    }

    private static TemplateOutput GenerateCommandRecord(CommandOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName}";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        if (o.ResultType is not null)
        {
            sb.AppendLine($"public sealed record {o.CommandName} : ICommand<{o.ResultType}>;");
        }
        else
        {
            sb.AppendLine($"public sealed record {o.CommandName} : ICommand;");
        }

        var path = $"src/{o.ModuleName}.Application/Commands/{o.CommandName}/{o.CommandName}.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateCommandHandler(CommandOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName}";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        if (o.ResultType is not null)
        {
            sb.AppendLine($"public sealed class {o.CommandName}Handler : ICommandHandler<{o.CommandName}, {o.ResultType}>");
            sb.AppendLine("{");
            sb.AppendLine($"    public Task<Result<{o.ResultType}>> Handle({o.CommandName} command, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: Implement command logic");
            sb.AppendLine("        throw new NotImplementedException();");
            sb.AppendLine("    }");
        }
        else
        {
            sb.AppendLine($"public sealed class {o.CommandName}Handler : ICommandHandler<{o.CommandName}>");
            sb.AppendLine("{");
            sb.AppendLine($"    public Task<Result> Handle({o.CommandName} command, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: Implement command logic");
            sb.AppendLine("        return Task.FromResult(Result.Success());");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Application/Commands/{o.CommandName}/{o.CommandName}Handler.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateValidator(CommandOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName}";

        sb.AppendLine("using FluentValidation;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {o.CommandName}Validator : AbstractValidator<{o.CommandName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public {o.CommandName}Validator()");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: Add validation rules");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Application/Commands/{o.CommandName}/{o.CommandName}Validator.cs";
        return new TemplateOutput(path, sb.ToString());
    }

    private static TemplateOutput GenerateUnitTest(CommandOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Tests.Unit.Commands";

        sb.AppendLine("using Modulus.Mediator.Abstractions;");
        sb.AppendLine("using Shouldly;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public class {o.CommandName}HandlerTests");
        sb.AppendLine("{");

        if (o.ResultType is not null)
        {
            sb.AppendLine("    [Fact]");
            sb.AppendLine("    public async Task Handle_should_return_success_with_value()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var handler = new {o.CommandName}Handler();");
            sb.AppendLine();
            sb.AppendLine("        // TODO: Implement test after handler logic is complete");
            sb.AppendLine("        await Should.ThrowAsync<NotImplementedException>(");
            sb.AppendLine($"            () => handler.Handle(new {o.CommandName}()));");
            sb.AppendLine("    }");
        }
        else
        {
            sb.AppendLine("    [Fact]");
            sb.AppendLine("    public async Task Handle_should_return_success()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var handler = new {o.CommandName}Handler();");
            sb.AppendLine();
            sb.AppendLine($"        var result = await handler.Handle(new {o.CommandName}());");
            sb.AppendLine();
            sb.AppendLine("        result.IsSuccess.ShouldBeTrue();");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        var path = $"tests/{o.ModuleName}.Tests.Unit/Commands/{o.CommandName}HandlerTests.cs";
        return new TemplateOutput(path, sb.ToString());
    }
}
