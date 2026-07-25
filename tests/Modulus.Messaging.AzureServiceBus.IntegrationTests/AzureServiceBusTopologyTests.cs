using Shouldly;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests;

// Plain [Fact]s, deliberately without [Trait("Category", "Integration")]: these are pure naming
// computations with no broker dependency, so they must run in the default (non-Docker) test
// filter alongside ConfigJsonDriftGuardTests-style guards.
public sealed class AzureServiceBusTopologyTests
{
    [Fact]
    public void TopicName_LowerCasesTheMessageTypeName()
    {
        AzureServiceBusTopology.TopicName("My.Namespace.SomeEvent").ShouldBe("my.namespace.someevent");
    }

    [Fact]
    public void TopicName_NestedTypePlusSeparator_IsFoldedToDot()
    {
        // Type.FullName uses '+' to separate a nested type from its declaring type; '+' is not
        // legal in a Service Bus entity name.
        AzureServiceBusTopology.TopicName("My.Namespace.Outer+Inner").ShouldBe("my.namespace.outer.inner");
    }

    [Fact]
    public void TopicName_OtherIllegalCharacters_AreFoldedToDot()
    {
        AzureServiceBusTopology.TopicName("My Namespace#Event!").ShouldBe("my.namespace.event.");
    }

    [Fact]
    public void TopicName_PreservesLegalCharacters()
    {
        AzureServiceBusTopology.TopicName("legal-name_1.2/3").ShouldBe("legal-name_1.2/3");
    }

    [Fact]
    public void TopicName_NameWithinLimit_DoesNotThrow()
    {
        var name = new string('a', 260);

        Should.NotThrow(() => AzureServiceBusTopology.TopicName(name));
    }

    [Fact]
    public void TopicName_NameExceeding260Characters_ThrowsDescriptiveError()
    {
        var tooLong = new string('a', 261);

        var ex = Should.Throw<InvalidOperationException>(() => AzureServiceBusTopology.TopicName(tooLong));

        ex.Message.ShouldContain("261");
        ex.Message.ShouldContain("260");
        ex.Message.ShouldContain("topic");
    }

    [Fact]
    public void SubscriptionName_NameWithinLimit_ReturnsSanitizedNameUnchanged()
    {
        AzureServiceBusTopology.SubscriptionName("orders-worker").ShouldBe("orders-worker");
    }

    [Fact]
    public void SubscriptionName_NameExceeding50Characters_IsTruncatedWithStableHashSuffix()
    {
        var longName = new string('a', 80);

        var result = AzureServiceBusTopology.SubscriptionName(longName);

        result.Length.ShouldBe(50);
        result.ShouldStartWith(new string('a', 41));
        result.ShouldContain("-");
    }

    [Fact]
    public void SubscriptionName_SameLongNameTwice_ProducesTheSameHash()
    {
        var longName = new string('b', 90);

        AzureServiceBusTopology.SubscriptionName(longName).ShouldBe(AzureServiceBusTopology.SubscriptionName(longName));
    }

    [Fact]
    public void SubscriptionName_DifferentLongNamesWithSamePrefix_DoNotCollide()
    {
        var first = new string('c', 90) + "-one";
        var second = new string('c', 90) + "-two";

        AzureServiceBusTopology.SubscriptionName(first).ShouldNotBe(AzureServiceBusTopology.SubscriptionName(second));
    }
}
