using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Endpoints;
using SampleApp.Orders.Application.Commands.CreateOrder;

namespace SampleApp.Orders.Api.Endpoints;

/// <summary>
/// POST /api/orders — dispatches <see cref="CreateOrder"/> through the mediator pipeline
/// (validation, unit of work) and returns the new order id.
/// </summary>
public sealed class PlaceOrder : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/", async (CreateOrder command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.Match(
                id => Results.Created($"/api/orders/{id}", id),
                ApiResults.Problem);
        })
        .WithName("PlaceOrder")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}
