using Shouldly;
using Xunit;

namespace Modulus.Messaging.RabbitMq.IntegrationTests;

// Plain [Fact]s, deliberately without [Trait("Category", "Integration")]: these are pure naming
// computations with no broker dependency, so they must run in the default (non-Docker) test
// filter alongside ConfigJsonDriftGuardTests-style guards.
public sealed class RabbitMqTopologyTests
{
    [Fact]
    public void ExchangeName_LowerCasesTheMessageTypeName()
    {
        RabbitMqTopology.ExchangeName("My.Namespace.SomeEvent").ShouldBe("my.namespace.someevent");
    }

    [Fact]
    public void QueueName_DelegatesToEndpointNameResolverSanitization()
    {
        // EndpointNameResolver.Sanitize lower-cases and maps anything outside
        // [a-z0-9.\-_] to '-'; QueueName must not alter that output.
        RabbitMqTopology.QueueName("My Endpoint!").ShouldBe("my-endpoint-");
    }

    [Fact]
    public void DeadLetterExchangeName_AppendsDlxSuffixToQueueName()
    {
        RabbitMqTopology.DeadLetterExchangeName("orders").ShouldBe("orders.dlx");
    }

    [Fact]
    public void DeadLetterQueueName_AppendsDeadLetterSuffixToQueueName()
    {
        RabbitMqTopology.DeadLetterQueueName("orders").ShouldBe("orders.dead-letter");
    }

    [Fact]
    public void ExchangeName_NameWithinLimit_DoesNotThrow()
    {
        var name = new string('a', 255);

        Should.NotThrow(() => RabbitMqTopology.ExchangeName(name));
    }

    [Fact]
    public void ExchangeName_NameExceeding255Utf8Bytes_ThrowsDescriptiveError()
    {
        var tooLong = new string('a', 256);

        var ex = Should.Throw<InvalidOperationException>(() => RabbitMqTopology.ExchangeName(tooLong));

        ex.Message.ShouldContain("256");
        ex.Message.ShouldContain("255");
        ex.Message.ShouldContain("exchange");
    }

    [Fact]
    public void ExchangeName_MultiByteCharacters_CountsUtf8BytesNotChars()
    {
        // Each 'é' is 2 UTF-8 bytes; 128 of them is 256 bytes but only 128 chars, so a
        // char-length check would miss this while a byte-length check must catch it.
        var name = new string('é', 128);

        var ex = Should.Throw<InvalidOperationException>(() => RabbitMqTopology.ExchangeName(name));

        ex.Message.ShouldContain("256");
    }

    [Fact]
    public void QueueName_EndpointNameExceedingByteLimit_ThrowsDescriptiveError()
    {
        var tooLong = new string('a', 256);

        var ex = Should.Throw<InvalidOperationException>(() => RabbitMqTopology.QueueName(tooLong));

        ex.Message.ShouldContain("queue");
    }

    [Fact]
    public void DeadLetterQueueName_BaseNameWithinLimitButSuffixPushesOverLimit_ThrowsDescriptiveError()
    {
        // 255 - ".dead-letter" (12 chars) + 1 is still within the queue's own limit, but the
        // suffixed dead-letter queue name must independently be guarded.
        var borderline = new string('a', 250);

        Should.NotThrow(() => RabbitMqTopology.QueueName(borderline));
        var ex = Should.Throw<InvalidOperationException>(() => RabbitMqTopology.DeadLetterQueueName(borderline));

        ex.Message.ShouldContain("dead-letter queue");
    }
}
