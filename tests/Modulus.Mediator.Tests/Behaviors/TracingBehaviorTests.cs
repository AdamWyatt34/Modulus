using System.Diagnostics;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Behaviors;

public class TracingBehaviorTests : IDisposable
{
    private readonly List<Activity> _completed = [];
    private readonly ActivityListener _listener;

    public TracingBehaviorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TracingBehavior<TestCommand, Result>.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _completed.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task Handle_Success_EmitsActivityWithSuccessOutcome()
    {
        var behavior = new TracingBehavior<TestCommand, Result>();

        var result = await behavior.Handle(
            new TestCommand("test"),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _completed.Count.ShouldBe(1);
        var activity = _completed[0];
        activity.DisplayName.ShouldBe(nameof(TestCommand));
        activity.GetTagItem("modulus.outcome").ShouldBe("success");
        activity.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task Handle_Failure_TagsErrorCodeAndCount()
    {
        var behavior = new TracingBehavior<TestCommand, Result>();

        var result = await behavior.Handle(
            new TestCommand("test"),
            () => Task.FromResult(Result.Failure(
                Error.Validation("Test.Invalid", "invalid"),
                Error.Validation("Test.Other", "other"))),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        _completed.Count.ShouldBe(1);
        var activity = _completed[0];
        activity.GetTagItem("modulus.outcome").ShouldBe("failure");
        activity.GetTagItem("modulus.error_count").ShouldBe(2);
        activity.GetTagItem("modulus.error_code").ShouldBe("Test.Invalid");
        activity.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Handle_Exception_SetsErrorStatusAndRethrows()
    {
        var behavior = new TracingBehavior<TestCommand, Result>();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new TestCommand("test"),
                () => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        _completed.Count.ShouldBe(1);
        var activity = _completed[0];
        activity.GetTagItem("modulus.outcome").ShouldBe("exception");
        activity.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Handle_NoListener_StillReturnsResponse()
    {
        _listener.Dispose();
        var behavior = new TracingBehavior<TestCommand, Result>();

        var result = await behavior.Handle(
            new TestCommand("test"),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
