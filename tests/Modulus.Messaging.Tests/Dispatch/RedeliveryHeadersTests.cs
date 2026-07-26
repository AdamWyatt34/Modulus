using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Dispatch;

public class RedeliveryHeadersTests
{
    private static TransportEnvelope Envelope(IReadOnlyDictionary<string, string>? headers = null) => new(
        "My.Event",
        Guid.NewGuid(),
        null,
        DateTime.UtcNow,
        "{}"u8.ToArray())
    {
        Headers = headers,
    };

    [Fact]
    public void GetAttempt_defaults_to_one_without_headers()
    {
        RedeliveryHeaders.GetAttempt(Envelope()).ShouldBe(1);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("garbage", 1)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    public void GetAttempt_parses_valid_values_and_falls_back_to_one(string raw, int expected)
    {
        var envelope = Envelope(new Dictionary<string, string> { ["modulus-delivery-attempt"] = raw });

        RedeliveryHeaders.GetAttempt(envelope).ShouldBe(expected);
    }

    [Fact]
    public void ForRedelivery_increments_attempt_and_preserves_other_headers()
    {
        var envelope = Envelope(new Dictionary<string, string>
        {
            ["modulus-delivery-attempt"] = "2",
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        });

        var headers = RedeliveryHeaders.ForRedelivery(envelope);

        headers["modulus-delivery-attempt"].ShouldBe("3");
        headers["traceparent"].ShouldBe("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        // .Keys.ShouldNotContain, not .ShouldNotContainKey: Shouldly 4.3.0's net8.0 build only
        // overloads that assertion for IDictionary<,>, not IReadOnlyDictionary<,> (the net9.0/
        // net10.0 build has both) — this form compiles identically on both TFMs.
        headers.Keys.ShouldNotContain("modulus-redeliver-endpoint");
    }

    [Fact]
    public void ForRedelivery_with_target_endpoint_stamps_it()
    {
        var headers = RedeliveryHeaders.ForRedelivery(Envelope(), targetEndpoint: "billing");

        headers["modulus-delivery-attempt"].ShouldBe("2");
        headers["modulus-redeliver-endpoint"].ShouldBe("billing");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("billing", false)]
    [InlineData("shipping", true)]
    public void IsForeignRedelivery_compares_the_target_endpoint(string? target, bool expected)
    {
        var headers = target is null
            ? null
            : new Dictionary<string, string> { ["modulus-redeliver-endpoint"] = target };

        RedeliveryHeaders.IsForeignRedelivery(Envelope(headers), "billing").ShouldBe(expected);
    }
}
