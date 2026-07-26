using System.Diagnostics;
using Modulus.Messaging.Diagnostics;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Diagnostics;

public sealed class TraceContextPropagationTests : IDisposable
{
    private readonly ActivitySource _source = new("TraceContextPropagationTests");
    private readonly ActivityListener _listener;

    public TraceContextPropagationTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "TraceContextPropagationTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void Inject_with_null_activity_returns_input_unchanged()
    {
        TraceContextPropagation.Inject(null).ShouldBeNull();

        var existing = new Dictionary<string, string> { ["custom"] = "value" };
        TraceContextPropagation.Inject(null, existing).ShouldBeSameAs(existing);
    }

    [Fact]
    public void Inject_writes_traceparent_and_preserves_existing_headers()
    {
        using var activity = _source.StartActivity("op");
        activity.ShouldNotBeNull();

        var headers = TraceContextPropagation.Inject(
            activity, new Dictionary<string, string> { ["custom"] = "value" });

        headers.ShouldNotBeNull();
        headers[MessagingDiagnostics.TraceParentHeader].ShouldBe(activity.Id);
        headers["custom"].ShouldBe("value");
        // .Keys.ShouldNotContain, not .ShouldNotContainKey: Shouldly 4.3.0's net8.0 build only
        // overloads that assertion for IDictionary<,>, not IReadOnlyDictionary<,> (the net9.0/
        // net10.0 build has both) — this form compiles identically on both TFMs.
        headers.Keys.ShouldNotContain(MessagingDiagnostics.TraceStateHeader);
    }

    [Fact]
    public void Inject_includes_tracestate_when_present()
    {
        using var activity = _source.StartActivity("op");
        activity.ShouldNotBeNull();
        activity.TraceStateString = "vendor=value";

        var headers = TraceContextPropagation.Inject(activity);

        headers.ShouldNotBeNull();
        headers[MessagingDiagnostics.TraceStateHeader].ShouldBe("vendor=value");
    }

    [Fact]
    public void Roundtrip_extracts_the_injected_context_as_remote()
    {
        using var activity = _source.StartActivity("op");
        activity.ShouldNotBeNull();
        activity.TraceStateString = "vendor=value";

        var headers = TraceContextPropagation.Inject(activity);

        TraceContextPropagation.TryExtract(headers, out var context).ShouldBeTrue();
        context.TraceId.ShouldBe(activity.TraceId);
        context.SpanId.ShouldBe(activity.SpanId);
        context.TraceState.ShouldBe("vendor=value");
        context.IsRemote.ShouldBeTrue();
    }

    [Fact]
    public void TryExtract_without_traceparent_returns_false()
    {
        TraceContextPropagation.TryExtract(null, out _).ShouldBeFalse();
        TraceContextPropagation.TryExtract(new Dictionary<string, string>(), out _).ShouldBeFalse();
        TraceContextPropagation
            .TryExtract(new Dictionary<string, string> { ["traceparent"] = "garbage" }, out _)
            .ShouldBeFalse();
    }
}
