using System.Text;

namespace Modulus.Templates;

/// <summary>
/// Programmatic code generator for minimal API endpoint scaffolding.
/// Produces a full IEndpoint class file for the file-per-endpoint pattern.
/// </summary>
public sealed class EndpointGenerator
{
    /// <summary>
    /// Generates a complete IEndpoint class file.
    /// </summary>
    public TemplateOutput Generate(EndpointOptions o)
    {
        var sb = new StringBuilder();
        var mapMethod = o.HttpMethod switch
        {
            "GET" => "MapGet",
            "POST" => "MapPost",
            "PUT" => "MapPut",
            "DELETE" => "MapDelete",
            _ => "MapGet",
        };

        // Using directives
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");

        if (o.CommandName is not null || o.QueryName is not null)
        {
            sb.AppendLine("using Modulus.Mediator.Abstractions;");
            sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Infrastructure.Endpoints;");
            sb.AppendLine($"using {o.SolutionName}.WebApi.Extensions;");
        }
        else
        {
            sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Infrastructure.Endpoints;");
        }

        if (o.CommandName is not null)
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName};");
        }

        if (o.QueryName is not null)
        {
            sb.AppendLine($"using {o.SolutionName}.{o.ModuleName}.Application.Queries.{o.QueryName};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {o.SolutionName}.{o.ModuleName}.Api.Endpoints;");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {o.EndpointName} : IEndpoint");
        sb.AppendLine("{");
        sb.AppendLine("    public void MapEndpoint(IEndpointRouteBuilder app)");
        sb.AppendLine("    {");

        if (o.QueryName is not null)
        {
            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async (IMediator mediator, CancellationToken ct) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var result = await mediator.Query(new {o.QueryName}(), ct);");
            sb.AppendLine("            return result.Match(Results.Ok, ApiResults.Problem);");
            sb.AppendLine("        })");
            sb.AppendLine($"        .WithName(\"{o.EndpointName}\")");
            sb.AppendLine($"        .Produces<{o.ResultType}>(StatusCodes.Status200OK)");
            sb.AppendLine("        .ProducesProblem(StatusCodes.Status500InternalServerError);");
        }
        else if (o.CommandName is not null)
        {
            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async (IMediator mediator, CancellationToken ct) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var result = await mediator.Send(new {o.CommandName}(), ct);");

            if (o.ResultType is not null && o.HttpMethod == "POST")
            {
                sb.AppendLine($"            return result.Match(");
                sb.AppendLine($"                value => Results.Created($\"/api/{o.ModuleName.ToLowerInvariant()}{o.Route}\", value),");
                sb.AppendLine("                ApiResults.Problem);");
            }
            else if (o.ResultType is not null)
            {
                sb.AppendLine("            return result.Match(Results.Ok, ApiResults.Problem);");
            }
            else
            {
                sb.AppendLine("            return result.Match(Results.NoContent, ApiResults.Problem);");
            }

            sb.AppendLine("        })");
            sb.AppendLine($"        .WithName(\"{o.EndpointName}\")");

            if (o.ResultType is not null)
            {
                if (o.HttpMethod == "POST")
                {
                    sb.AppendLine($"        .Produces<{o.ResultType}>(StatusCodes.Status201Created)");
                }
                else
                {
                    sb.AppendLine($"        .Produces<{o.ResultType}>(StatusCodes.Status200OK)");
                }
            }
            else
            {
                sb.AppendLine("        .Produces(StatusCodes.Status204NoContent)");
            }

            sb.AppendLine("        .ProducesProblem(StatusCodes.Status500InternalServerError);");
        }
        else
        {
            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async (CancellationToken ct) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            // TODO: Wire up to a command or query");
            sb.AppendLine("            return Results.Ok();");
            sb.AppendLine("        })");
            sb.AppendLine($"        .WithName(\"{o.EndpointName}\");");
        }

        sb.AppendLine("    }");
        sb.Append("}");
        sb.AppendLine();

        var relativePath = $"Endpoints/{o.EndpointName}.cs";
        return new TemplateOutput(relativePath, sb.ToString());
    }
}
