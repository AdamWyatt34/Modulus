using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Endpoints;
using SampleApp.Orders.Application.Queries.GetOrder;

namespace SampleApp.Orders.Api.Endpoints;

/// <summary>
/// GET /api/orders/{id} — dispatches <see cref="GetOrder"/> and maps the Result to 200/404.
/// </summary>
public sealed class GetOrderById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Query(new GetOrder(id), ct);
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .WithName("GetOrderById")
        .Produces<OrderDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}
