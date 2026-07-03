using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for integration event scaffolding.
/// Produces a single <c>IntegrationEvent</c> record in the module's Integration project.
/// </summary>
public sealed class EventGenerator
{
    public IReadOnlyList<TemplateOutput> Generate(EventOptions options)
    {
        return [GenerateEventRecord(options)];
    }

    private static TemplateOutput GenerateEventRecord(EventOptions o)
    {
        var sb = new StringBuilder();
        var ns = $"{o.SolutionName}.{o.ModuleName}.Integration.IntegrationEvents";

        // BuildingBlocks.Integration's global using does not propagate to referencing
        // projects, so the using must be explicit here.
        sb.AppendLine("using Modulus.Messaging.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        if (o.Properties.Count > 0)
        {
            var parameters = string.Join(", ", o.Properties.Select(p => $"{p.Type} {p.Name}"));
            sb.AppendLine($"public sealed record {o.EventName}({parameters}) : IntegrationEvent;");
        }
        else
        {
            sb.AppendLine($"public sealed record {o.EventName} : IntegrationEvent;");
        }

        var path = $"src/{o.ModuleName}.Integration/IntegrationEvents/{o.EventName}.cs";
        return new TemplateOutput(path, sb.ToString());
    }
}
