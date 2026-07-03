using Modulus.Messaging.AzureServiceBus;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.AzureServiceBus;

public class AzureServiceBusTopologyTests
{
    [Fact]
    public void TopicName_IsLowerCasedStableTypeName()
    {
        AzureServiceBusTopology.TopicName("MyApp.Orders.OrderCreatedEvent")
            .ShouldBe("myapp.orders.ordercreatedevent");
    }

    [Fact]
    public void SubscriptionName_ShortName_PassesThroughSanitized()
    {
        AzureServiceBusTopology.SubscriptionName("Checkout Service")
            .ShouldBe("checkout-service");
    }

    [Fact]
    public void SubscriptionName_LongName_TruncatesTo50WithStableHash()
    {
        var longName = new string('a', 80);

        var first = AzureServiceBusTopology.SubscriptionName(longName);
        var second = AzureServiceBusTopology.SubscriptionName(longName);

        first.Length.ShouldBe(50);
        first.ShouldBe(second);
        first[41].ShouldBe('-');
    }

    [Fact]
    public void SubscriptionName_DistinctLongNames_DoNotCollide()
    {
        var nameA = new string('a', 60) + "-service-one";
        var nameB = new string('a', 60) + "-service-two";

        AzureServiceBusTopology.SubscriptionName(nameA)
            .ShouldNotBe(AzureServiceBusTopology.SubscriptionName(nameB));
    }
}
