using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.InMemory;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Retention;
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

        if (options.OutboxClaimLease < TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxClaimLease,
                "OutboxClaimLease must be at least 30 seconds.");

        if (options.OutboxClaimLease <= options.OutboxPollInterval)
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxClaimLease,
                "OutboxClaimLease must be greater than OutboxPollInterval, or a lease can expire " +
                "mid-dispatch and race a concurrent claimant over the same rows.");

        if (options.PrefetchCount is <= 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), options.PrefetchCount,
                "PrefetchCount must be between 1 and 1000.");

        if (options.ConsumerReservationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.ConsumerReservationTimeout,
                "ConsumerReservationTimeout must be positive and exceed the worst-case handler execution time.");

        ValidateRetryPolicy(options.RetryPolicy, nameof(MessagingOptions.RetryPolicy));
        ValidateRetryPolicy(options.ConsumerRetry, nameof(MessagingOptions.ConsumerRetry));
        ValidateRetention(options.Retention);
        ValidateTransportConfiguration(options);

        // Must run before anything below reads options.Assemblies (MessageTypeRegistry,
        // DiscoverHandlers here, and OutboxDispatcher's own allowlist scan once resolved from
        // this same singleton): a duplicate entry — e.g. two configure callbacks each adding
        // typeof(Program).Assembly — would otherwise scan and register every handler and event
        // mapping in it twice, silently double-invoking every handler for that assembly.
        DeduplicateAssemblies(options);

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
        // Lenient: hosts without metrics DI (no IMeterFactory) still get a working meter.
        services.AddSingleton(provider => new MessagingMetrics(provider.GetService<IMeterFactory>()));
        services.AddSingleton<ConsumerDispatcher>();

        services.AddScoped<IMessageBus, TransportMessageBus>();
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddScoped<IOutboxAdminStore, EfOutboxAdminStore>();
        services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();
        // TryAdd: a custom wake source (e.g. a database change listener package) may
        // pre-register its own notifier decorator before calling AddModulusMessaging.
        services.TryAddSingleton<IOutboxNotifier, OutboxNotifier>();
        services.TryAddSingleton<OutboxNotifyingInterceptor>();

        // Consumer host first: its subscriptions must exist before the outbox processor's first
        // dispatch pass (the in-memory transport drops messages published with no subscriber).
        // Hosted services stop in reverse order, so shutdown stops the outbox first and then
        // drains in-flight consumers.
        services.AddHostedService<TransportConsumerHost>();
        services.AddHostedService<OutboxProcessor>();

        if (options.Retention.Enabled)
            services.AddHostedService<MessagingRetentionService>();

        return services;
    }

    private static IMessageTransport CreateTransport(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<MessagingOptions>();

        if (options.Transport == Transport.InMemory)
            return new InMemoryTransport(provider.GetRequiredService<ILogger<InMemoryTransport>>(), options);

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
    /// <remarks>
    /// When <see cref="AddModulusMessaging(IServiceCollection, Action{MessagingOptions})"/> is
    /// also registered, <see cref="OutboxNotifyingInterceptor"/> is attached automatically so
    /// rows saved through this context wake the outbox processor immediately. Application
    /// contexts that map the outbox table themselves get the same behavior with
    /// <c>options.AddInterceptors(sp.GetRequiredService&lt;OutboxNotifyingInterceptor&gt;())</c>.
    /// </remarks>
    public static IServiceCollection AddModulusOutbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<OutboxDbContext>((provider, optionsBuilder) =>
        {
            configure(optionsBuilder);

            // GetService, not GetRequiredService: keeps this call order-independent and
            // usable without AddModulusMessaging (the interceptor is registered there).
            var interceptor = provider.GetService<OutboxNotifyingInterceptor>();
            if (interceptor is not null)
                optionsBuilder.AddInterceptors(interceptor);
        });
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
        services.AddScoped<IInboxAdminStore, EfInboxAdminStore>();
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

    // Mutates the same List<Assembly> instance MessagingOptions.Assemblies returns (its
    // property getter is read-only, but the backing list is not), so every reader that holds
    // a reference to this options instance — including one resolved later from DI — sees the
    // deduped set without needing its own copy.
    private static void DeduplicateAssemblies(MessagingOptions options)
    {
        var distinct = options.Assemblies.Distinct().ToList();
        if (distinct.Count == options.Assemblies.Count)
            return;

        options.Assemblies.Clear();
        options.Assemblies.AddRange(distinct);
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

    private static void ValidateRetention(RetentionOptions retention)
    {
        ArgumentNullException.ThrowIfNull(retention);

        if (!retention.Enabled)
            return;

        if (retention.ProcessedOutboxAge < TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(retention), retention.ProcessedOutboxAge,
                "Retention.ProcessedOutboxAge must be at least 1 minute.");

        if (retention.InboxAge < TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(retention), retention.InboxAge,
                "Retention.InboxAge must be at least 1 minute; it must also exceed the broker's maximum redelivery horizon.");

        if (retention.SweepInterval < TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(retention), retention.SweepInterval,
                "Retention.SweepInterval must be at least 1 minute.");

        if (retention.PurgeBatchSize is <= 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(retention), retention.PurgeBatchSize,
                "Retention.PurgeBatchSize must be between 1 and 10000.");
    }

    private static void ValidateRetryPolicy(RetryPolicyOptions retryPolicy, string optionName)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);

        // MaxAttempts < 1 would starve the outbox (EfOutboxStore.ClaimPending filters Attempts < MaxAttempts).
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
