using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class AzureServiceBusOptionsTests
{
    [Fact]
    public void AzureServiceBus_NoConnectionStringNoCredential_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Should.Throw<InvalidOperationException>(() =>
            services.AddModulusMessaging(o =>
            {
                o.Transport = Transport.AzureServiceBus;
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));
    }

    [Fact]
    public void AzureServiceBus_CredentialWithoutFqns_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Should.Throw<InvalidOperationException>(() =>
            services.AddModulusMessaging(o =>
            {
                o.Transport = Transport.AzureServiceBus;
                o.Credential = new FakeTokenCredential();
                // FullyQualifiedNamespace deliberately omitted.
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));
    }

    [Fact]
    public void AzureServiceBus_ConnectionStringOnly_DoesNotThrowAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Connection string is syntactically valid; we don't connect during registration.
        Should.NotThrow(() =>
            services.AddModulusMessaging(o =>
            {
                o.Transport = Transport.AzureServiceBus;
                o.ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));
    }

    [Fact]
    public void AzureServiceBus_CredentialWithFqns_DoesNotThrowAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Should.NotThrow(() =>
            services.AddModulusMessaging(o =>
            {
                o.Transport = Transport.AzureServiceBus;
                o.Credential = new FakeTokenCredential();
                o.FullyQualifiedNamespace = "myns.servicebus.windows.net";
                o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            }));
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("fake", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
