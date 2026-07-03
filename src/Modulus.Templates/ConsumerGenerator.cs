using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for integration event consumer scaffolding.
/// Produces a single <c>IIntegrationEventHandler&lt;TEvent&gt;</c> in the consuming
/// module's Infrastructure project. The handler is auto-discovered and registered by
/// the Modulus source generator; no manual DI registration is required.
/// </summary>
public sealed class ConsumerGenerator
{
    public TemplateOutput Generate(ConsumerOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Infrastructure.IntegrationEventHandlers";

        sb.AppendLine("using Modulus.Messaging.Abstractions;");
        sb.AppendLine($"using {o.EventNamespace};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {o.EventName}Handler : IIntegrationEventHandler<{o.EventName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public Task Handle({o.EventName} @event, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: Implement integration event handling logic");
        sb.AppendLine("        return Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"src/{o.ModuleName}.Infrastructure/IntegrationEventHandlers/{o.EventName}Handler.cs";
        return new TemplateOutput(path, sb.ToString());
    }
}
