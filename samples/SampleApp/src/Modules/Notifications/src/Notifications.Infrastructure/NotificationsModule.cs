using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Registration;
using SampleApp.Notifications.Api.Endpoints;
using SampleApp.Notifications.Application.Data;
using SampleApp.Notifications.Infrastructure.Persistence;

namespace SampleApp.Notifications.Infrastructure;

public sealed class NotificationsModule : IModuleRegistration
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The scaffold defaults to UseSqlServer(...GetConnectionString("Default")); this sample
        // uses SQLite so it runs anywhere with zero infrastructure.
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Notifications")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddDbContext<NotificationsReadOnlyDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Notifications")));

        services.AddScoped<IQueryDb>(sp => sp.GetRequiredService<NotificationsReadOnlyDbContext>());

        return services;
    }

    public static IEndpointRouteBuilder ConfigureEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapNotificationsEndpoints();
        return endpoints;
    }
}
