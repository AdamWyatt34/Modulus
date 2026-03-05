using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus mediator. Use the source-generated <c>AddModulusHandlers()</c>
    /// extension method to register command, query, and event handlers.
    /// </summary>
    public static IServiceCollection AddModulusMediator(this IServiceCollection services)
    {
        services.AddScoped<IMediator, Mediator>();
        return services;
    }

    /// <summary>
    /// Registers an open-generic pipeline behavior that wraps every mediator request.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="behaviorType">An open-generic type implementing <see cref="IPipelineBehavior{TRequest, TResponse}"/>.</param>
    public static IServiceCollection AddPipelineBehavior(
        this IServiceCollection services,
        Type behaviorType)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), behaviorType);
        return services;
    }
}
