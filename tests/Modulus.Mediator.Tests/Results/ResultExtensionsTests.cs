using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Results;

public class ResultExtensionsTests
{
    private static readonly Error TestError = Error.Validation("Test.Error", "Something went wrong");

    private static Task<Result> SuccessTask() => Task.FromResult(Result.Success());

    private static Task<Result> FailureTask() => Task.FromResult(Result.Failure(TestError));

    private static Task<Result<int>> SuccessTask(int value) => Task.FromResult(Result<int>.Success(value));

    private static Task<Result<int>> FailureTaskT() => Task.FromResult(Result<int>.Failure(TestError));

    // ── Task<Result> sources ─────────────────────────────────────

    [Fact]
    public async Task Bind_on_task_success_invokes_next()
    {
        var result = await SuccessTask().Bind(() => Result<int>.Success(42));

        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Bind_on_task_failure_propagates_errors()
    {
        var result = await FailureTask().Bind(() => Result<int>.Success(42));

        result.IsFailure.ShouldBeTrue();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public async Task Bind_on_task_success_with_async_next_awaits_it()
    {
        var result = await SuccessTask().Bind(() => Task.FromResult(Result.Success()));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Map_on_task_success_produces_value()
    {
        var result = await SuccessTask().Map(() => "ok");

        result.Value.ShouldBe("ok");
    }

    [Fact]
    public async Task Tap_on_task_success_runs_side_effect()
    {
        var invoked = false;

        var result = await SuccessTask().Tap(() => invoked = true);

        invoked.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Ensure_on_task_success_with_failing_predicate_fails()
    {
        var result = await SuccessTask().Ensure(() => false, TestError);

        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public async Task Match_on_task_failure_calls_onFailure()
    {
        var output = await FailureTask().Match(
            () => "ok",
            r => $"fail:{r.FirstError.Code}");

        output.ShouldBe("fail:Test.Error");
    }

    // ── Task<Result<T>> sources ──────────────────────────────────

    [Fact]
    public async Task Bind_on_taskT_success_passes_value()
    {
        var result = await SuccessTask(21).Bind(v => Result<int>.Success(v * 2));

        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Bind_on_taskT_failure_skips_next()
    {
        var invoked = false;

        var result = await FailureTaskT().Bind(v =>
        {
            invoked = true;
            return Result<int>.Success(v);
        });

        invoked.ShouldBeFalse();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public async Task Bind_on_taskT_success_with_async_next_awaits_it()
    {
        var result = await SuccessTask(1).Bind(v => Task.FromResult(Result.Success()));

        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result>();
    }

    [Fact]
    public async Task Map_on_taskT_success_transforms_value()
    {
        var result = await SuccessTask(42).Map(v => v.ToString());

        result.Value.ShouldBe("42");
    }

    [Fact]
    public async Task Map_on_taskT_success_with_async_map_awaits_it()
    {
        var result = await SuccessTask(6).Map(v => Task.FromResult(v * 7));

        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Tap_on_taskT_success_sees_value()
    {
        var seen = 0;

        await SuccessTask(42).Tap(v => seen = v);

        seen.ShouldBe(42);
    }

    [Fact]
    public async Task Ensure_on_taskT_failure_keeps_original_errors()
    {
        var result = await FailureTaskT().Ensure(v => true, Error.Failure("Other", "Other"));

        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public async Task Ensure_on_taskT_with_error_factory_builds_error_from_value()
    {
        var result = await SuccessTask(-3).Ensure(v => v > 0, v => Error.Validation("Neg", $"{v} is negative"));

        result.FirstError.Description.ShouldBe("-3 is negative");
    }

    [Fact]
    public async Task Match_on_taskT_success_receives_value()
    {
        var output = await SuccessTask(42).Match(
            v => $"value:{v}",
            r => "fail");

        output.ShouldBe("value:42");
    }

    [Fact]
    public async Task Match_on_taskT_with_async_branches_awaits_them()
    {
        var output = await SuccessTask(42).Match(
            v => Task.FromResult(v * 2),
            r => Task.FromResult(0));

        output.ShouldBe(84);
    }

    // ── Fluent pipeline ──────────────────────────────────────────

    [Fact]
    public async Task Full_async_pipeline_chains_without_intermediate_awaits()
    {
        var result = await SuccessTask(10)
            .Ensure(v => v > 0, TestError)
            .Map(v => v * 2)
            .Bind(v => Task.FromResult(Result<string>.Success($"total:{v}")))
            .Tap(_ => { });

        result.Value.ShouldBe("total:20");
    }

    [Fact]
    public async Task Full_async_pipeline_short_circuits_on_failure()
    {
        var stepsRun = 0;

        var result = await FailureTaskT()
            .Map(v =>
            {
                stepsRun++;
                return v;
            })
            .Bind(v =>
            {
                stepsRun++;
                return Result<int>.Success(v);
            });

        stepsRun.ShouldBe(0);
        result.FirstError.ShouldBe(TestError);
    }
}
