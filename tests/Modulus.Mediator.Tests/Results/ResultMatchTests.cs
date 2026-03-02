using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Results;

public class ResultMatchTests
{
    // ── Result (non-generic) ─────────────────────────────────────

    [Fact]
    public void Match_on_success_result_calls_onSuccess()
    {
        var result = Result.Success();

        var output = result.Match(
            () => "ok",
            r => $"fail:{r.Errors[0].Code}");

        output.ShouldBe("ok");
    }

    [Fact]
    public void Match_on_failure_result_calls_onFailure()
    {
        var result = Result.Failure(Error.NotFound("Item.NotFound", "Not found"));

        var output = result.Match(
            () => "ok",
            r => $"fail:{r.Errors[0].Code}");

        output.ShouldBe("fail:Item.NotFound");
    }

    // ── Result<T> ────────────────────────────────────────────────

    [Fact]
    public void Match_on_success_resultT_calls_onSuccess_with_value()
    {
        var result = Result<int>.Success(42);

        var output = result.Match(
            value => $"value:{value}",
            r => $"fail:{r.Errors[0].Code}");

        output.ShouldBe("value:42");
    }

    [Fact]
    public void Match_on_failure_resultT_calls_onFailure()
    {
        var result = Result<int>.Failure(Error.Validation("Bad", "Invalid"));

        var output = result.Match(
            value => $"value:{value}",
            r => $"fail:{r.Errors[0].Code}");

        output.ShouldBe("fail:Bad");
    }

    [Fact]
    public void Match_on_success_result_returns_correct_type()
    {
        var result = Result.Success();

        var output = result.Match(
            () => 200,
            _ => 500);

        output.ShouldBe(200);
    }

    [Fact]
    public void Match_on_failure_resultT_receives_all_errors()
    {
        var result = Result<string>.Failure(
            Error.Validation("E1", "First"),
            Error.Validation("E2", "Second"));

        var output = result.Match(
            _ => 0,
            r => r.Errors.Count);

        output.ShouldBe(2);
    }
}
