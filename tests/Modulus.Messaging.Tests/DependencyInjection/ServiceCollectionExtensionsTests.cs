using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RabbitMq_without_connection_string_throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddModulusMessaging(options =>
            {
                options.Transport = Transport.RabbitMq;
                // ConnectionString intentionally omitted
            });
        });

        ex.Message.ShouldContain("ConnectionString");
    }

    [Fact]
    public void AzureServiceBus_without_connection_string_throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddModulusMessaging(options =>
            {
                options.Transport = Transport.AzureServiceBus;
                // ConnectionString intentionally omitted
            });
        });

        ex.Message.ShouldContain("ConnectionString");
    }
}
