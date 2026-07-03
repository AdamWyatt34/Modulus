using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SampleApp.BuildingBlocks.Infrastructure.Endpoints;

namespace SampleApp.Orders.Api.Endpoints;

public static class OrdersEndpointRegistration
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        // SECURITY: When you wire an authentication scheme (JWT bearer, OIDC, Keycloak, etc.)
        // in WebApi/Program.cs, replace the line below with the commented version to require
        // auth on every endpoint in this module. Individual endpoints can then opt out with
        // .AllowAnonymous() in their MapEndpoint call.
        //
        //   var group = app.MapGroup("/api/orders").RequireAuthorization();
        //
        // The default below is anonymous because Program.cs ships with AddAuthentication()
        // alone (no registered scheme) — calling RequireAuthorization without a scheme would
        // throw "No authenticationScheme was specified" on the first request.
        var group = app.MapGroup("/api/orders");

        var endpoints = typeof(OrdersEndpointRegistration).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IEndpoint).IsAssignableFrom(t))
            .Select(Activator.CreateInstance)
            .Cast<IEndpoint>();

        foreach (var endpoint in endpoints)
        {
            endpoint.MapEndpoint(group);
        }

        return app;
    }
}
