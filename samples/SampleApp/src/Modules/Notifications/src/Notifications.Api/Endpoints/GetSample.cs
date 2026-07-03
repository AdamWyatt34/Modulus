using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Endpoints;
using SampleApp.Notifications.Application.Samples;

namespace SampleApp.Notifications.Api.Endpoints;

public sealed class GetSample : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Once NotificationsEndpointRegistration enables .RequireAuthorization() on the parent
        // group, append .AllowAnonymous() below to keep this sample endpoint public, or remove
        // this comment when you're satisfied with the inherited policy.
        app.MapGet("/sample", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Query(new GetSampleQuery(), ct);
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .WithName("GetNotificationsSample")
        .Produces<string>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}
