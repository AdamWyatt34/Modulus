using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.InMemory;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus messaging infrastructure including the message bus, outbox processor,
    /// inbox idempotency store, and the consumer pipeline for handlers discovered from the
    /// assemblies specified in <see cref="MessagingOptions"/>.
    /// </summary>
    /// <remarks>
    /// This registers <see cref="IOutboxStore"/> and <see cref="IInboxStore"/> against the
    /// library's <see cref="OutboxDbContext"/> and <see cref="InboxDbContext"/>. Consumers must
    /// separately call <see cref="AddModulusOutbox(IServiceCollection, Action{DbContextOptionsBuilder})"/>
    /// and <see cref="AddModulusInbox(IServiceCollection, Action{DbContextOptionsBuilder})"/>
    /// to wire the database contexts, then apply the schema migrations.
    /// Broker transports ship as separate packages: install ModulusKit.Messaging.RabbitMq or
    /// ModulusKit.Messaging.AzureServiceBus and call its <c>AddModulus*Transport()</c> extension.
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

        if (options.PrefetchCount is <= 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), options.PrefetchCount,
                "PrefetchCount must be between 1 and 1000.");

        if (options.ConsumerReservationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.ConsumerReservationTimeout,
                "ConsumerReservationTimeout must be positive and exceed the worst-case handler execution time.");

        ValidateRetryPolicy(options.RetryPolicy, nameof(MessagingOptions.RetryPolicy));
        ValidateRetryPolicy(options.ConsumerRetry, nameof(MessagingOptions.ConsumerRetry));
        ValidateTransportConfiguration(options);

        // Empty Assemblies is allowed: publish-only hosts use IMessageBus directly and need no consumers.
        services.AddSingleton(options);

        var typeRegistry = new MessageTypeRegistry(options.Assemblies);
        services.AddSingleton(typeRegistry);

        var handlerRegistrations = DiscoverHandlers(options.Assemblies);

        foreach (var registration in handlerRegistrations)
        {
            services.AddScoped(registration.HandlerInterface, registration.HandlerImplementation);
        }

        var subscriptions = handlerRegistrations
            .Select(registration => registration.EventType)
            .Distinct()
            .Select(eventType => new TransportSubscription(eventType, typeRegistry.GetName(eventType)))
            .ToList();
        services.AddSingleton(new TransportSubscriptionCatalog(subscriptions));

        services.AddSingleton(CreateTransport);
        services.AddSingleton<ConsumerDispatcher>();

        services.AddScoped<IMessageBus, TransportMessageBus>();
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddScoped<IOutboxAdminStore, EfOutboxAdminStore>();
        services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();

        // Consumer host first: its subscriptions must exist before the outbox processor's first
        // dispatch pass (the in-memory transport drops messages published with no subscriber).
        // Hosted services stop in reverse order, so shutdown stops the outbox first and then
        // drains in-flight consumers.
        services.AddHostedService<TransportConsumerHost>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    private static IMessageTransport CreateTransport(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<MessagingOptions>();

        if (options.Transport == Transport.InMemory)
            return new InMemoryTransport(provider.GetRequiredService<ILogger<InMemoryTransport>>());

        var factory = provider
            .GetServices<ITransportFactory>()
            .FirstOrDefault(candidate => candidate.Transport == options.Transport);

        return factory is not null
            ? factory.Create(provider, options)
            : throw new InvalidOperationException(options.Transport switch
            {
                Transport.RabbitMq =>
                    "No RabbitMQ transport is registered. Install the ModulusKit.Messaging.RabbitMq package " +
                    "and call services.AddModulusRabbitMqTransport().",
                Transport.AzureServiceBus =>
                    "No Azure Service Bus transport is registered. Install the ModulusKit.Messaging.AzureServiceBus " +
                    "package and call services.AddModulusAzureServiceBusTransport().",
                _ => $"Unsupported transport type: {options.Transport}.",
            });
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
    /// the consumer pipeline falls through to direct handler execution with no deduplication.
    /// </summary>
    public static IServiceCollection AddModulusInbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<InboxDbContext>(configure);
        services.AddScoped<IInboxStore, EfInboxStore>();
        return services;
    }

    private static void ValidateTransportConfiguration(MessagingOptions options)
    {
        switch (options.Transport)
        {
            case Transport.InMemory:
                break;

            case Transport.RabbitMq:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    throw new InvalidOperationException(
                        "ConnectionString is required for RabbitMQ transport.");
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

    private sealed record HandlerRegistration(
        Type EventType,
        Type HandlerInterface,
        Type HandlerImplementation);
}
