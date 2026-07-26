using Azure.Messaging.ServiceBus;
using Modulus.Messaging.AzureServiceBus;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.AzureServiceBus;

public class AzureServiceBusEnvelopeHeaderTests
{
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private static TransportEnvelope EnvelopeWithHeaders(IReadOnlyDictionary<string, string>? headers) => new(
        "My.Event",
        Guid.NewGuid(),
        null,
        new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
        "{}"u8.ToArray())
    {
        Headers = headers,
    };

    private static ServiceBusReceivedMessage Received(Dictionary<string, object> properties)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            subject: "My.Event",
            contentType: "application/json",
            properties: properties);

    [Fact]
    public void ToServiceBusMessage_writes_envelope_headers_as_application_properties()
    {
        var envelope = EnvelopeWithHeaders(new Dictionary<string, string>
        {
            ["traceparent"] = TraceParent,
            ["tracestate"] = "vendor=x",
        });

        var message = AzureServiceBusEnvelopeMapper.ToServiceBusMessage(envelope);

        message.ApplicationProperties["traceparent"].ShouldBe(TraceParent);
        message.ApplicationProperties["tracestate"].ShouldBe("vendor=x");
        message.ApplicationProperties.ShouldContainKey(AzureServiceBusEnvelopeMapper.OccurredOnProperty);
    }

    [Fact]
    public void ToEnvelope_harvests_string_application_properties_except_occurred_on()
    {
        var received = Received(new Dictionary<string, object>
        {
            ["traceparent"] = TraceParent,
            ["custom-header"] = "custom-value",
            ["numeric-property"] = 42,
            [AzureServiceBusEnvelopeMapper.OccurredOnProperty] = "2026-07-01T12:00:00.0000000Z",
        });

        var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(received);

        envelope.Headers.ShouldNotBeNull();
        envelope.Headers["traceparent"].ShouldBe(TraceParent);
        envelope.Headers["custom-header"].ShouldBe("custom-value");
        envelope.Headers.ShouldNotContainKey("numeric-property");
        envelope.Headers.ShouldNotContainKey(AzureServiceBusEnvelopeMapper.OccurredOnProperty);
    }

    [Fact]
    public void ToEnvelope_falls_back_to_the_sdk_diagnostic_id_for_traceparent()
    {
        // A non-Modulus publisher instrumented by the Azure SDK stamps Diagnostic-Id only.
        var received = Received(new Dictionary<string, object>
        {
            [AzureServiceBusEnvelopeMapper.DiagnosticIdProperty] = TraceParent,
        });

        var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(received);

        envelope.Headers.ShouldNotBeNull();
        envelope.Headers["traceparent"].ShouldBe(TraceParent);
    }

    [Fact]
    public void ToEnvelope_prefers_an_explicit_traceparent_over_diagnostic_id()
    {
        var received = Received(new Dictionary<string, object>
        {
            ["traceparent"] = TraceParent,
            [AzureServiceBusEnvelopeMapper.DiagnosticIdProperty] = "00-ffffffffffffffffffffffffffffffff-ffffffffffffffff-01",
        });

        var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(received);

        envelope.Headers!["traceparent"].ShouldBe(TraceParent);
    }

    [Fact]
    public void ToEnvelope_with_no_application_properties_yields_null_headers()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            subject: "My.Event");

        AzureServiceBusEnvelopeMapper.ToEnvelope(received).Headers.ShouldBeNull();
    }
}
