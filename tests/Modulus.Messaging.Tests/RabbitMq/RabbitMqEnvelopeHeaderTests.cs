using Modulus.Messaging.RabbitMq;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.RabbitMq;

public class RabbitMqEnvelopeHeaderTests
{
    private static TransportEnvelope EnvelopeWithHeaders(IReadOnlyDictionary<string, string>? headers) => new(
        "My.Event",
        Guid.NewGuid(),
        null,
        new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
        "{}"u8.ToArray())
    {
        Headers = headers,
    };

    [Fact]
    public void ToBasicProperties_writes_envelope_headers_alongside_occurred_on()
    {
        var envelope = EnvelopeWithHeaders(new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ["tracestate"] = "vendor=x",
        });

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(envelope);

        properties.Headers!["traceparent"].ShouldBe("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        properties.Headers["tracestate"].ShouldBe("vendor=x");
        properties.Headers.ShouldContainKey(RabbitMqEnvelopeMapper.OccurredOnHeader);
    }

    [Fact]
    public void RoundTrip_preserves_headers_delivered_as_byte_arrays()
    {
        var original = EnvelopeWithHeaders(new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ["custom-header"] = "custom-value",
        });

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(original);

        // The broker delivers string headers as UTF-8 byte arrays; simulate that for every header.
        foreach (var key in properties.Headers!.Keys.ToList())
        {
            if (properties.Headers[key] is string s)
                properties.Headers[key] = System.Text.Encoding.UTF8.GetBytes(s);
        }

        var roundTripped = RabbitMqEnvelopeMapper.ToEnvelope(properties, original.Body);

        roundTripped.Headers.ShouldNotBeNull();
        roundTripped.Headers["traceparent"].ShouldBe("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        roundTripped.Headers["custom-header"].ShouldBe("custom-value");
        // The occurred-on transport detail is not an envelope header.
        // .Keys.ShouldNotContain, not .ShouldNotContainKey: Shouldly 4.3.0's net8.0 build only
        // overloads that assertion for IDictionary<,>, not IReadOnlyDictionary<,> (the net9.0/
        // net10.0 build has both) — this form compiles identically on both TFMs.
        roundTripped.Headers.Keys.ShouldNotContain(RabbitMqEnvelopeMapper.OccurredOnHeader);
        roundTripped.OccurredOn.ShouldBe(original.OccurredOn);
    }

    [Fact]
    public void ToEnvelope_with_only_occurred_on_header_yields_null_headers()
    {
        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(EnvelopeWithHeaders(null));

        var roundTripped = RabbitMqEnvelopeMapper.ToEnvelope(properties, "{}"u8.ToArray());

        roundTripped.Headers.ShouldBeNull();
    }
}
