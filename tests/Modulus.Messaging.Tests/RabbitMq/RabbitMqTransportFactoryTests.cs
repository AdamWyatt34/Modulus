using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.RabbitMq;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.RabbitMq;

public class RabbitMqTransportFactoryTests
{
    // Placeholder pointing at a local dev broker; not a credential.
    private const string LocalBrokerUri = "amqp://localhost:5672/";

    [Fact]
    public void Create_MissingConnectionString_ThrowsWithGuidance()
    {
        var factory = new RabbitMqTransportFactory();
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            factory.Create(provider, new MessagingOptions { Transport = Transport.RabbitMq }));

        exception.Message.ShouldContain("ConnectionString");
    }

    [Fact]
    public void Create_WithConnectionString_ReturnsTransport()
    {
        var factory = new RabbitMqTransportFactory();
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var options = new MessagingOptions { Transport = Transport.RabbitMq };
        options.ConnectionString = LocalBrokerUri;

        var transport = factory.Create(provider, options);

        transport.ShouldBeOfType<RabbitMqTransport>();
    }

    [Fact]
    public void Factory_AdvertisesRabbitMqTransport()
    {
        new RabbitMqTransportFactory().Transport.ShouldBe(Transport.RabbitMq);
    }

    [Fact]
    public void AddModulusRabbitMqTransport_RegistersFactoryOnce()
    {
        var services = new ServiceCollection();

        services.AddModulusRabbitMqTransport();
        services.AddModulusRabbitMqTransport();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ITransportFactory>().Count().ShouldBe(1);
    }
}
