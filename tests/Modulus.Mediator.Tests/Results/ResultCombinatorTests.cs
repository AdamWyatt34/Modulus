using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Results;

public class ResultCombinatorTests
{
    private static readonly Error TestError = Error.Validation("Test.Error", "Something went wrong");
    private static readonly Error OtherError = Error.NotFound("Test.Other", "Not found");

    // ── FirstError ───────────────────────────────────────────────

    [Fact]
    public void FirstError_on_failure_returns_first_error()
    {
        var result = Result.Failure(TestError, OtherError);

        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void FirstError_on_success_throws()
    {
        var result = Result.Success();

        Should.Throw<InvalidOperationException>(() => result.FirstError);
    }

    [Fact]
    public void FirstError_on_failed_resultT_returns_first_error()
    {
        var result = Result<int>.Failure(OtherError, TestError);

        result.FirstError.ShouldBe(OtherError);
    }

    // ── Bind (Result) ────────────────────────────────────────────

    [Fact]
    public void Bind_on_success_result_invokes_next()
    {
        var result = Result.Success().Bind(() => Result.Failure(TestError));

        result.IsFailure.ShouldBeTrue();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void Bind_on_failure_result_skips_next_and_propagates_errors()
    {
        var invoked = false;
        var result = Result.Failure(TestError).Bind(() =>
        {
            invoked = true;
            return Result.Success();
        });

        invoked.ShouldBeFalse();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void Bind_on_success_result_to_valued_result_produces_value()
    {
        var result = Result.Success().Bind(() => Result<int>.Success(42));

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Bind_on_failure_result_to_valued_result_propagates_errors()
    {
        var result = Result.Failure(TestError, OtherError).Bind(() => Result<int>.Success(42));

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void Bind_with_null_delegate_throws()
    {
        Should.Throw<ArgumentNullException>(() => Result.Success().Bind((Func<Result>)null!));
    }

    // ── Bind (Result<T>) ─────────────────────────────────────────

    [Fact]
    public void Bind_on_success_resultT_passes_value_to_next()
    {
        var result = Result<int>.Success(21).Bind(v => Result<int>.Success(v * 2));

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Bind_on_failure_resultT_skips_next_and_propagates_errors()
    {
        var invoked = false;
        var result = Result<int>.Failure(TestError).Bind(v =>
        {
            invoked = true;
            return Result<string>.Success(v.ToString());
        });

        invoked.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void Bind_on_success_resultT_to_valueless_result()
    {
        var result = Result<int>.Success(1).Bind(_ => Result.Success());

        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result>();
    }

    // ── BindAsync ────────────────────────────────────────────────

    [Fact]
    public async Task BindAsync_on_success_result_awaits_next()
    {
        var result = await Result.Success().BindAsync(() => Task.FromResult(Result<int>.Success(7)));

        result.Value.ShouldBe(7);
    }

    [Fact]
    public async Task BindAsync_on_failure_resultT_skips_next()
    {
        var invoked = false;
        var result = await Result<int>.Failure(TestError).BindAsync(v =>
        {
            invoked = true;
            return Task.FromResult(Result<int>.Success(v));
        });

        invoked.ShouldBeFalse();
        result.FirstError.ShouldBe(TestError);
    }

    // ── Map ──────────────────────────────────────────────────────

    [Fact]
    public void Map_on_success_resultT_transforms_value()
    {
        var result = Result<int>.Success(42).Map(v => v.ToString());

        result.Value.ShouldBe("42");
    }

    [Fact]
    public void Map_on_failure_resultT_propagates_errors()
    {
        var result = Result<int>.Failure(TestError).Map(v => v.ToString());

        result.IsFailure.ShouldBeTrue();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void Map_on_success_result_produces_value()
    {
        var result = Result.Success().Map(() => 42);

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Map_returning_null_throws()
    {
        Should.Throw<ArgumentNullException>(() => Result<int>.Success(1).Map(_ => (string)null!));
    }

    [Fact]
    public async Task MapAsync_on_success_resultT_transforms_value()
    {
        var result = await Result<int>.Success(6).MapAsync(v => Task.FromResult(v * 7));

        result.Value.ShouldBe(42);
    }

    // ── Tap ──────────────────────────────────────────────────────

    [Fact]
    public void Tap_on_success_resultT_runs_side_effect_and_returns_same_result()
    {
        var seen = 0;
        var source = Result<int>.Success(42);

        var result = source.Tap(v => seen = v);

        seen.ShouldBe(42);
        result.ShouldBeSameAs(source);
    }

    [Fact]
    public void Tap_on_failure_result_skips_side_effect()
    {
        var invoked = false;

        var result = Result.Failure(TestError).Tap(() => invoked = true);

        invoked.ShouldBeFalse();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public async Task TapAsync_on_success_result_runs_side_effect()
    {
        var invoked = false;

        var result = await Result.Success().TapAsync(() =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        invoked.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
    }

    // ── Ensure ───────────────────────────────────────────────────

    [Fact]
    public void Ensure_on_success_with_passing_predicate_stays_success()
    {
        var result = Result<int>.Success(42).Ensure(v => v > 0, TestError);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Ensure_on_success_with_failing_predicate_fails_with_error()
    {
        var result = Result<int>.Success(-1).Ensure(v => v > 0, TestError);

        result.IsFailure.ShouldBeTrue();
        result.FirstError.ShouldBe(TestError);
    }

    [Fact]
    public void Ensure_on_failure_skips_predicate_and_keeps_original_errors()
    {
        var invoked = false;

        var result = Result<int>.Failure(OtherError).Ensure(v =>
        {
            invoked = true;
            return true;
        }, TestError);

        invoked.ShouldBeFalse();
        result.FirstError.ShouldBe(OtherError);
    }

    [Fact]
    public void Ensure_with_error_factory_builds_error_from_value()
    {
        var result = Result<int>.Success(-5)
            .Ensure(v => v > 0, v => Error.Validation("Test.Negative", $"Value {v} must be positive"));

        result.FirstError.Description.ShouldBe("Value -5 must be positive");
    }

    [Fact]
    public void Ensure_on_valueless_success_with_failing_predicate_fails()
    {
        var result = Result.Success().Ensure(() => false, TestError);

        result.FirstError.ShouldBe(TestError);
    }

    // ── MatchAsync ───────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_on_success_result_calls_onSuccess()
    {
        var output = await Result.Success().MatchAsync(
            () => Task.FromResult("ok"),
            r => Task.FromResult($"fail:{r.FirstError.Code}"));

        output.ShouldBe("ok");
    }

    [Fact]
    public async Task MatchAsync_on_failure_resultT_calls_onFailure()
    {
        var output = await Result<int>.Failure(TestError).MatchAsync(
            v => Task.FromResult($"value:{v}"),
            r => Task.FromResult($"fail:{r.FirstError.Code}"));

        output.ShouldBe("fail:Test.Error");
    }

    // ── Chaining ─────────────────────────────────────────────────

    [Fact]
    public void Combinators_chain_success_through_multiple_steps()
    {
        var result = Result<int>.Success(10)
            .Ensure(v => v > 0, TestError)
            .Map(v => v * 2)
            .Bind(v => Result<string>.Success($"total:{v}"))
            .Tap(_ => { });

        result.Value.ShouldBe("total:20");
    }

    [Fact]
    public void Combinators_short_circuit_on_first_failure()
    {
        var stepsRun = 0;

        var result = Result<int>.Success(10)
            .Ensure(v => v < 0, TestError)
            .Map(v =>
            {
                stepsRun++;
                return v * 2;
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
