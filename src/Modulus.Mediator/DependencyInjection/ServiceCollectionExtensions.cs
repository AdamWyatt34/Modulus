using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus mediator and scans the calling assembly for command, query, and event handlers.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddModulusMediator(this IServiceCollection services)
    {
        return services.AddModulusMediator(Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Registers the Modulus mediator and scans the specified assemblies for command, query, and event handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan for handler implementations.</param>
    public static IServiceCollection AddModulusMediator(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddScoped<IMediator, Mediator>();

        services.Scan(scan =>
        {
            var selector = scan.FromAssemblies(assemblies);

            selector.AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime();

            selector.AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime();

            selector.AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime();

            selector.AddClasses(classes => classes.AssignableTo(typeof(IStreamQueryHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime();

            selector.AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime();
        });

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
