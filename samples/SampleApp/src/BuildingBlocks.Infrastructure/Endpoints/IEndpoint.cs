using Microsoft.AspNetCore.Routing;

namespace SampleApp.BuildingBlocks.Infrastructure.Endpoints;

/// <summary>
/// Marker interface for a single minimal API endpoint.
/// Implement one class per endpoint and register via assembly scanning.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
