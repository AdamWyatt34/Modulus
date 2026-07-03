using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class ConfigurationBindingTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Bind_Transport_And_ConnectionString_FromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "RabbitMq",
            ["Messaging:ConnectionString"] = "amqp://guest:guest@localhost:5672",
        });

        services.AddModulusMessaging(configuration, o =>
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly));

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.Transport.ShouldBe(Transport.RabbitMq);
        options.ConnectionString.ShouldBe("amqp://guest:guest@localhost:5672");
    }

    [Fact]
    public void Bind_Scalar_And_RetryPolicy_Options_FromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:OutboxBatchSize"] = "250",
            ["Messaging:OutboxPollInterval"] = "00:00:10",
            ["Messaging:RetryPolicy:MaxAttempts"] = "7",
            ["Messaging:RetryPolicy:InitialInterval"] = "00:00:02",
        });

        services.AddModulusMessaging(configuration, o =>
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly));

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.OutboxBatchSize.ShouldBe(250);
        options.OutboxPollInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.RetryPolicy.MaxAttempts.ShouldBe(7);
        options.RetryPolicy.InitialInterval.ShouldBe(TimeSpan.FromSeconds(2));
        // Unset retry keys keep their defaults.
        options.RetryPolicy.MaxInterval.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Bind_InMemoryTransport_WithMinimalSection_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "InMemory",
        });

        Should.NotThrow(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));
    }

    [Fact]
    public void Bind_EmptySection_DefaultsToInMemory()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // No "Messaging" section present at all — every option keeps its default.
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        services.AddModulusMessaging(configuration, o =>
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly));

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.Transport.ShouldBe(Transport.InMemory);
        options.OutboxBatchSize.ShouldBe(100);
        options.OutboxPollInterval.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Bind_EndpointAndTransportTuning_FromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:EndpointName"] = "checkout-service",
            ["Messaging:PrefetchCount"] = "25",
            ["Messaging:AutoProvision"] = "false",
        });

        services.AddModulusMessaging(configuration, o =>
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly));

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.EndpointName.ShouldBe("checkout-service");
        options.PrefetchCount.ShouldBe(25);
        options.AutoProvision.ShouldBeFalse();
    }

    [Fact]
    public void Bind_InvalidTransportName_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "Kafka",
        });

        Should.Throw<InvalidOperationException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));
    }

    [Fact]
    public void Bind_RabbitMq_WithoutConnectionString_StillThrowsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "RabbitMq",
            // ConnectionString intentionally omitted — validation runs through the binder path too.
        });

        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));

        ex.Message.ShouldContain("ConnectionString");
    }

    [Fact]
    public void Callback_OverridesBoundValues()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "RabbitMq",
            ["Messaging:OutboxBatchSize"] = "250",
        });

        // The callback runs after binding, so it wins.
        services.AddModulusMessaging(configuration, o =>
        {
            o.Transport = Transport.InMemory;
            o.OutboxBatchSize = 50;
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.Transport.ShouldBe(Transport.InMemory);
        options.OutboxBatchSize.ShouldBe(50);
    }

    [Fact]
    public void Callback_SuppliesAzureCredential_ForConfigBoundTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Transport comes from config; the credential + FQNS can only come from the callback.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "AzureServiceBus",
        });

        Should.NotThrow(() =>
            services.AddModulusMessaging(configuration, o =>
            {
                o.Credential = new FakeTokenCredential();
                o.FullyQualifiedNamespace = "myns.servicebus.windows.net";
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            services.AddModulusMessaging((IConfiguration)null!, _ => { }));
    }

    [Fact]
    public void NullConfigureCallback_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        Should.Throw<ArgumentNullException>(() =>
            services.AddModulusMessaging(configuration, null!));
    }

    [Fact]
    public void ConfigOverload_WithNoAssemblies_RegistersForPublishOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "InMemory",
        });

        // Publish-only hosts use IMessageBus directly and need no consumer assemblies.
        Should.NotThrow(() => services.AddModulusMessaging(configuration, _ => { }));
        services.BuildServiceProvider().GetService<IMessageBus>().ShouldNotBeNull();
    }

    [Fact]
    public void ImperativeOverload_WithNoAssemblies_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Should.NotThrow(() => services.AddModulusMessaging(o => o.Transport = Transport.InMemory));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Bind_RetryPolicy_MaxAttemptsBelowOne_Throws(string maxAttempts)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:RetryPolicy:MaxAttempts"] = maxAttempts,
        });

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));

        ex.Message.ShouldContain("MaxAttempts");
    }

    [Fact]
    public void Bind_ConsumerRetry_MaxAttemptsBelowOne_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // ConsumerRetry is validated independently of RetryPolicy.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:ConsumerRetry:MaxAttempts"] = "0",
        });

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));

        ex.Message.ShouldContain("ConsumerRetry");
    }

    [Fact]
    public void Bind_ConsumerRetry_IsIndependentOfRetryPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:RetryPolicy:MaxAttempts"] = "5",
            ["Messaging:ConsumerRetry:MaxAttempts"] = "2",
        });

        services.AddModulusMessaging(configuration, o =>
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly));

        var options = services.BuildServiceProvider().GetRequiredService<MessagingOptions>();
        options.RetryPolicy.MaxAttempts.ShouldBe(5);
        options.ConsumerRetry.MaxAttempts.ShouldBe(2);
    }

    [Fact]
    public void ImperativeOverload_RetryPolicy_MaxAttemptsZero_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(o =>
            {
                o.RetryPolicy.MaxAttempts = 0;
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));

        ex.Message.ShouldContain("MaxAttempts");
    }

    [Fact]
    public void Bind_RetryPolicy_NegativeInterval_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:RetryPolicy:InitialInterval"] = "-00:00:01",
        });

        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));
    }

    [Fact]
    public void Bind_RetryPolicy_MaxIntervalBelowInitial_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:RetryPolicy:InitialInterval"] = "00:00:30",
            ["Messaging:RetryPolicy:MaxInterval"] = "00:00:05",
        });

        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(configuration, o =>
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly)));
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("fake", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
