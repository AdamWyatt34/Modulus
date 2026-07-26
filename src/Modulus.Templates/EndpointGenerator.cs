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

        // Route parameters (e.g. "{itemId:guid}" -> Guid itemId) are bound as leading lambda
        // parameters and forwarded positionally into the wired command/query's constructor —
        // the convention documented on `modulus add-endpoint` is that the target record declares
        // matching positional parameters, in the same order they appear in the route.
        var routeParams = RouteTemplateParser.Parse(o.Route);
        var routeParamDecls = string.Join(", ", routeParams.Select(p => $"{p.ClrType} {p.Name}"));
        var routeArgs = string.Join(", ", routeParams.Select(p => p.Name));

        // Using directives
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");

        if (o.CommandName is not null || o.QueryName is not null)
        {
            sb.AppendLine("using Modulus.Mediator.Abstractions;");
        }

        // NOTE: the module's Api project cannot reference the host WebApi project (that would be
        // a MOD001 violation — a backward, host-to-module-only reference is the only supported
        // shape), and nothing below needs a WebApi-namespace using anyway.
        sb.AppendLine($"using {o.SolutionName}.BuildingBlocks.Infrastructure.Endpoints;");

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
            var lambdaParams = routeParams.Count > 0
                ? $"{routeParamDecls}, IMediator mediator, CancellationToken ct"
                : "IMediator mediator, CancellationToken ct";

            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async ({lambdaParams}) =>");
            sb.AppendLine("        {");
            // global::-qualified: an endpoint named after its query (a natural choice) would
            // otherwise have `new {QueryName}` resolve to the enclosing endpoint class itself.
            sb.AppendLine($"            var result = await mediator.Query(new global::{o.SolutionName}.{o.ModuleName}.Application.Queries.{o.QueryName}.{o.QueryName}({routeArgs}), ct);");
            sb.AppendLine("            return result.Match(Results.Ok, ApiResults.Problem);");
            sb.AppendLine("        })");
            sb.AppendLine($"        .WithName(\"{o.EndpointName}\")");
            sb.AppendLine($"        .Produces<{o.ResultType}>(StatusCodes.Status200OK)");
            sb.AppendLine("        .ProducesProblem(StatusCodes.Status500InternalServerError);");
        }
        else if (o.CommandName is not null)
        {
            var lambdaParams = routeParams.Count > 0
                ? $"{routeParamDecls}, IMediator mediator, CancellationToken ct"
                : "IMediator mediator, CancellationToken ct";

            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async ({lambdaParams}) =>");
            sb.AppendLine("        {");
            // global::-qualified for the same self-name-shadowing reason as the query shape.
            sb.AppendLine($"            var result = await mediator.Send(new global::{o.SolutionName}.{o.ModuleName}.Application.Commands.{o.CommandName}.{o.CommandName}({routeArgs}), ct);");

            if (o.ResultType is not null && o.HttpMethod == "POST")
            {
                // The Location template is resolved here (generation time) with every route
                // parameter segment rewritten to a bare "{name}" hole
                // (RouteTemplateParser.ToInterpolationTemplate) before being spliced into the
                // generated file's own interpolated string. A raw "{id:guid}" segment would
                // otherwise be parsed by *that* interpolated string as an alignment/format-string
                // clause ("id" formatted with the non-existent "guid" format), which throws at
                // runtime even though it compiles.
                var locationTemplate = "/api/" + o.ModuleName.ToLowerInvariant()
                    + RouteTemplateParser.ToInterpolationTemplate(o.Route);
                var interpolate = routeParams.Count > 0 ? "$" : "";

                sb.AppendLine($"            return result.Match(");
                sb.AppendLine($"                value => Results.Created({interpolate}\"{locationTemplate}\", value),");
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
            var lambdaParams = routeParams.Count > 0
                ? $"{routeParamDecls}, CancellationToken ct"
                : "CancellationToken ct";

            sb.AppendLine($"        app.{mapMethod}(\"{o.Route}\", async ({lambdaParams}) =>");
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
