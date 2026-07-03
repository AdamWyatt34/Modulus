using Modulus.Messaging.RabbitMq;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.RabbitMq;

public class RabbitMqTopologyTests
{
    [Fact]
    public void ExchangeName_IsLowerCasedStableTypeName()
    {
        RabbitMqTopology.ExchangeName("MyApp.Orders.OrderCreatedEvent")
            .ShouldBe("myapp.orders.ordercreatedevent");
    }

    [Fact]
    public void QueueName_SanitizesToBrokerSafeCharacters()
    {
        RabbitMqTopology.QueueName("My App/Web:Api")
            .ShouldBe("my-app-web-api");
    }

    [Fact]
    public void DeadLetterNames_DeriveFromEndpointName()
    {
        RabbitMqTopology.DeadLetterExchangeName("checkout").ShouldBe("checkout.dlx");
        RabbitMqTopology.DeadLetterQueueName("checkout").ShouldBe("checkout.dead-letter");
    }

    [Fact]
    public void SendQueueName_SanitizesCommandTypeName()
    {
        RabbitMqTopology.SendQueueName("ProvisionTenantCommand")
            .ShouldBe("provisiontenantcommand");
    }
}
