using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Registration;
using SampleApp.Orders.Api.Endpoints;
using SampleApp.Orders.Application.Data;
using SampleApp.Orders.Infrastructure.Persistence;

namespace SampleApp.Orders.Infrastructure;

public sealed class OrdersModule : IModuleRegistration
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The scaffold defaults to UseSqlServer(...GetConnectionString("Default")); this sample
        // uses SQLite so it runs anywhere with zero infrastructure.
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Orders")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());

        services.AddDbContext<OrdersReadOnlyDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Orders")));

        services.AddScoped<IQueryDb>(sp => sp.GetRequiredService<OrdersReadOnlyDbContext>());

        return services;
    }

    public static IEndpointRouteBuilder ConfigureEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOrdersEndpoints();
        return endpoints;
    }
}
