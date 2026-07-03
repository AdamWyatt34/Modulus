using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.AzureServiceBus;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.AzureServiceBus;

public class AzureServiceBusTransportFactoryTests
{
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("fake", DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Create_NoCredentialAndNoConnectionString_Throws()
    {
        var factory = new AzureServiceBusTransportFactory();
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            factory.Create(provider, new MessagingOptions { Transport = Transport.AzureServiceBus }));

        exception.Message.ShouldContain("ConnectionString or Credential");
    }

    [Fact]
    public void Create_CredentialWithoutNamespace_Throws()
    {
        var factory = new AzureServiceBusTransportFactory();
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            factory.Create(provider, new MessagingOptions
            {
                Transport = Transport.AzureServiceBus,
                Credential = new FakeCredential(),
            }));

        exception.Message.ShouldContain("FullyQualifiedNamespace");
    }

    [Fact]
    public void Create_CredentialWithNamespace_ReturnsTransport()
    {
        var factory = new AzureServiceBusTransportFactory();
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var transport = factory.Create(provider, new MessagingOptions
        {
            Transport = Transport.AzureServiceBus,
            Credential = new FakeCredential(),
            FullyQualifiedNamespace = "myns.servicebus.windows.net",
        });

        transport.ShouldBeOfType<AzureServiceBusTransport>();
    }

    [Fact]
    public void Factory_AdvertisesAzureServiceBusTransport()
    {
        new AzureServiceBusTransportFactory().Transport.ShouldBe(Transport.AzureServiceBus);
    }

    [Fact]
    public void AddModulusAzureServiceBusTransport_RegistersFactoryOnce()
    {
        var services = new ServiceCollection();

        services.AddModulusAzureServiceBusTransport();
        services.AddModulusAzureServiceBusTransport();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ITransportFactory>().Count().ShouldBe(1);
    }
}
