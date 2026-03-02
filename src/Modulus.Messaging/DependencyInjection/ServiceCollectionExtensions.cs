using System.Reflection;
using MassTransit;
using MassTransit.ExtensionsDependencyInjectionIntegration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Outbox;

namespace Modulus.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus messaging infrastructure including the message bus, outbox processor,
    /// and consumer adapters discovered from the assemblies specified in <see cref="MessagingOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure <see cref="MessagingOptions"/>.</param>
    public static IServiceCollection AddModulusMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configure)
    {
        var options = new MessagingOptions();
        configure(options);

        services.AddSingleton(options);

        var handlerRegistrations = DiscoverHandlers(options.Assemblies);

        foreach (var registration in handlerRegistrations)
        {
            services.AddScoped(registration.HandlerInterface, registration.HandlerImplementation);
        }

        services.AddMassTransit(busConfigurator =>
        {
            foreach (var registration in handlerRegistrations)
            {
                var adapterType = typeof(IdempotentConsumerAdapter<>)
                    .MakeGenericType(registration.EventType);

                busConfigurator.AddConsumer(adapterType);
            }

            ConfigureTransport(busConfigurator, options);
        });

        services.AddScoped<IMessageBus, MassTransitMessageBus>();
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    private static void ConfigureTransport(
        IServiceCollectionBusConfigurator busConfigurator,
        MessagingOptions options)
    {
        switch (options.Transport)
        {
            case Transport.InMemory:
                busConfigurator.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
                break;

            case Transport.RabbitMq:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    throw new InvalidOperationException(
                        "ConnectionString is required for RabbitMQ transport.");

                busConfigurator.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(options.ConnectionString);
                    cfg.ConfigureEndpoints(context);
                });
                break;

            case Transport.AzureServiceBus:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    throw new InvalidOperationException(
                        "ConnectionString is required for Azure Service Bus transport.");

                busConfigurator.UsingAzureServiceBus((context, cfg) =>
                {
                    cfg.Host(options.ConnectionString);
                    cfg.ConfigureEndpoints(context);
                });
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Transport,
                    "Unsupported transport type.");
        }
    }

    private static List<HandlerRegistration> DiscoverHandlers(List<Assembly> assemblies)
    {
        var registrations = new List<HandlerRegistration>();
        var handlerInterfaceType = typeof(IIntegrationEventHandler<>);

        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false });

            foreach (var type in types)
            {
                var handlerInterfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType &&
                                i.GetGenericTypeDefinition() == handlerInterfaceType);

                foreach (var handlerInterface in handlerInterfaces)
                {
                    var eventType = handlerInterface.GetGenericArguments()[0];
                    registrations.Add(new HandlerRegistration(
                        eventType,
                        handlerInterface,
                        type));
                }
            }
        }

        return registrations;
    }

    private sealed record HandlerRegistration(
        Type EventType,
        Type HandlerInterface,
        Type HandlerImplementation);
}
