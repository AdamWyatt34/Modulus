using System.Diagnostics.Metrics;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Behaviors;

public class MetricsBehaviorTests
{
    private readonly TestMeterFactory _meterFactory;

    public MetricsBehaviorTests()
    {
        _meterFactory = new TestMeterFactory();
    }

    [Fact]
    public async Task Records_duration_on_success()
    {
        var behavior = new MetricsBehavior<TestCommand, Result>(_meterFactory);
        var measurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            // Filter to THIS test's factory: the histogram cache keeps instruments alive across
            // tests and the sibling MetricsBehaviorTests class runs in a parallel collection, so
            // name-only matching records other tests' measurements (flaky on 2-core CI runners).
            if (instrument.Name == "modulus.mediator.handler.duration" && _meterFactory.Owns(instrument.Meter))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var result = await behavior.Handle(
            new TestCommand("test"),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        measurements.ShouldNotBeEmpty();
        var recorded = measurements[0];
        recorded.Value.ShouldBeGreaterThanOrEqualTo(0);
        recorded.Tags.ShouldContain(t => t.Key == "handler" && (string)t.Value! == "TestCommand");
        recorded.Tags.ShouldContain(t => t.Key == "outcome" && (string)t.Value! == "success");
    }

    [Fact]
    public async Task Records_duration_on_failure()
    {
        var behavior = new MetricsBehavior<TestCommand, Result>(_meterFactory);
        var measurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            // Filter to THIS test's factory: the histogram cache keeps instruments alive across
            // tests and the sibling MetricsBehaviorTests class runs in a parallel collection, so
            // name-only matching records other tests' measurements (flaky on 2-core CI runners).
            if (instrument.Name == "modulus.mediator.handler.duration" && _meterFactory.Owns(instrument.Meter))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var result = await behavior.Handle(
            new TestCommand("test"),
            _ => Task.FromResult(Result.Failure(Error.Failure("Test", "fail"))),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        measurements.ShouldNotBeEmpty();
        measurements[0].Tags.ShouldContain(t => t.Key == "outcome" && (string)t.Value! == "failure");
    }

    [Fact]
    public async Task Records_duration_on_exception()
    {
        var behavior = new MetricsBehavior<TestCommand, Result>(_meterFactory);
        var measurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            // Filter to THIS test's factory: the histogram cache keeps instruments alive across
            // tests and the sibling MetricsBehaviorTests class runs in a parallel collection, so
            // name-only matching records other tests' measurements (flaky on 2-core CI runners).
            if (instrument.Name == "modulus.mediator.handler.duration" && _meterFactory.Owns(instrument.Meter))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await behavior.Handle(
                new TestCommand("test"),
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None);
        });

        measurements.ShouldNotBeEmpty();
        measurements[0].Tags.ShouldContain(t => t.Key == "outcome" && (string)t.Value! == "exception");
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public bool Owns(Meter meter)
        {
            lock (_meters)
                return _meters.Contains(meter);
        }

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            lock (_meters)
                _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
                meter.Dispose();
        }
    }
}
