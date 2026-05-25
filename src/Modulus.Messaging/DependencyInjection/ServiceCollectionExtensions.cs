using System.Reflection;
using GreenPipes;
using MassTransit;
using MassTransit.ExtensionsDependencyInjectionIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Outbox;

namespace Modulus.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus messaging infrastructure including the message bus, outbox processor,
    /// inbox idempotency store, and consumer adapters discovered from the assemblies specified
    /// in <see cref="MessagingOptions"/>.
    /// </summary>
    /// <remarks>
    /// This registers <see cref="IOutboxStore"/> and <see cref="IInboxStore"/> against the
    /// library's <see cref="OutboxDbContext"/> and <see cref="InboxDbContext"/>. Consumers must
    /// separately call <see cref="AddModulusOutbox(IServiceCollection, Action{DbContextOptionsBuilder})"/>
    /// and <see cref="AddModulusInbox(IServiceCollection, Action{DbContextOptionsBuilder})"/>
    /// to wire the database contexts, then apply the schema migrations.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure <see cref="MessagingOptions"/>.</param>
    public static IServiceCollection AddModulusMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MessagingOptions();
        configure(options);

        return AddModulusMessagingCore(services, options);
    }

    /// <summary>
    /// Registers the Modulus messaging infrastructure, binding <see cref="MessagingOptions"/> from the
    /// "Messaging" configuration section (<see cref="MessagingOptions.SectionName"/>) and then applying the
    /// <paramref name="configure"/> callback.
    /// </summary>
    /// <remarks>
    /// The callback runs after binding, so it can override bound values and supply members that cannot be
    /// bound from configuration — <see cref="MessagingOptions.Assemblies"/> (consumer hosts add their handler
    /// assembly; publish-only hosts may leave it empty) and <see cref="MessagingOptions.Credential"/>.
    /// It is required so callers consciously make that choice.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration to bind the "Messaging" section from.</param>
    /// <param name="configure">A delegate to add assemblies/credential and override any bound values.</param>
    public static IServiceCollection AddModulusMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MessagingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MessagingOptions();
        configuration.GetSection(MessagingOptions.SectionName).Bind(options);
        configure(options);

        return AddModulusMessagingCore(services, options);
    }

    private static IServiceCollection AddModulusMessagingCore(
        IServiceCollection services,
        MessagingOptions options)
    {
        if (options.OutboxBatchSize is <= 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxBatchSize,
                "OutboxBatchSize must be between 1 and 1000.");

        if (options.OutboxPollInterval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxPollInterval,
                "OutboxPollInterval must be at least 1 second.");

        ValidateRetryPolicy(options.RetryPolicy, nameof(MessagingOptions.RetryPolicy));
        ValidateRetryPolicy(options.ConsumerRetry, nameof(MessagingOptions.ConsumerRetry));

        // Empty Assemblies is allowed: publish-only hosts use IMessageBus directly and need no consumers.
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
        services.AddScoped<IOutboxAdminStore, EfOutboxAdminStore>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="OutboxDbContext"/> with the specified configuration.
    /// Required for the outbox processor to read/write integration events.
    /// </summary>
    public static IServiceCollection AddModulusOutbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<OutboxDbContext>(configure);
        return services;
    }

    /// <summary>
    /// Registers <see cref="InboxDbContext"/> and <see cref="IInboxStore"/> with the specified
    /// database configuration. Required to enable consumer idempotency — without this call,
    /// <see cref="Internals.IdempotentConsumerAdapter{TEvent}"/> falls through to direct handler
    /// execution with no deduplication.
    /// </summary>
    public static IServiceCollection AddModulusInbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<InboxDbContext>(configure);
        services.AddScoped<IInboxStore, EfInboxStore>();
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
                    ApplyRetryPolicy(cfg, options.ConsumerRetry);
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
                    ApplyRetryPolicy(cfg, options.ConsumerRetry);
                    cfg.ConfigureEndpoints(context);
                });
                break;

            case Transport.AzureServiceBus:
                if (options.Credential is not null)
                {
                    if (string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace))
                        throw new InvalidOperationException(
                            "FullyQualifiedNamespace is required when Credential is provided for Azure Service Bus.");
                }
                else if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException(
                        "ConnectionString or Credential + FullyQualifiedNamespace is required for Azure Service Bus transport.");
                }

                busConfigurator.UsingAzureServiceBus((context, cfg) =>
                {
                    if (options.Credential is not null)
                    {
                        cfg.Host(options.FullyQualifiedNamespace!, h =>
                        {
                            h.TokenCredential = options.Credential;
                        });
                    }
                    else
                    {
                        cfg.Host(options.ConnectionString);
                    }
                    ApplyRetryPolicy(cfg, options.ConsumerRetry);
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
            var types = assembly.GetTypesSafe()
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

    private static void ValidateRetryPolicy(RetryPolicyOptions retryPolicy, string optionName)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);

        // MaxAttempts < 1 would starve the outbox (EfOutboxStore.GetPending filters Attempts < MaxAttempts).
        if (retryPolicy.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(optionName, retryPolicy.MaxAttempts,
                $"{optionName}.MaxAttempts must be at least 1.");

        if (retryPolicy.InitialInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(optionName, retryPolicy.InitialInterval,
                $"{optionName}.InitialInterval must not be negative.");

        if (retryPolicy.MaxInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(optionName, retryPolicy.MaxInterval,
                $"{optionName}.MaxInterval must not be negative.");

        if (retryPolicy.IntervalIncrement < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(optionName, retryPolicy.IntervalIncrement,
                $"{optionName}.IntervalIncrement must not be negative.");

        if (retryPolicy.MaxInterval < retryPolicy.InitialInterval)
            throw new ArgumentOutOfRangeException(optionName, retryPolicy.MaxInterval,
                $"{optionName}.MaxInterval must be greater than or equal to {optionName}.InitialInterval.");
    }

    private static void ApplyRetryPolicy(IBusFactoryConfigurator cfg, RetryPolicyOptions retryPolicy)
    {
        // MaxAttempts in our options includes the original attempt; MassTransit's Exponential
        // retryCount is the number of *additional* retries after the first attempt.
        var additionalRetries = Math.Max(0, retryPolicy.MaxAttempts - 1);
        if (additionalRetries == 0)
            return;

        cfg.UseMessageRetry(r => r.Exponential(
            additionalRetries,
            retryPolicy.InitialInterval,
            retryPolicy.MaxInterval,
            retryPolicy.IntervalIncrement));
    }

    private sealed record HandlerRegistration(
        Type EventType,
        Type HandlerInterface,
        Type HandlerImplementation);
}
