using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Results;

public class ResultTests
{
    // ── Result (non-generic) ─────────────────────────────────────

    [Fact]
    public void Success_result_has_empty_errors()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Failure_result_with_multiple_errors_preserves_all()
    {
        var errors = new[]
        {
            Error.Validation("Field1", "Required"),
            Error.Validation("Field2", "Too long"),
            Error.NotFound("Entity", "Not found")
        };

        var result = Result.Failure(errors);

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(3);
        result.Errors[0].Code.ShouldBe("Field1");
        result.Errors[1].Code.ShouldBe("Field2");
        result.Errors[2].Code.ShouldBe("Entity");
    }

    [Fact]
    public void Failure_from_IEnumerable_errors_works()
    {
        IEnumerable<Error> errors = [Error.Failure("E1", "First"), Error.Failure("E2", "Second")];

        var result = Result.Failure(errors);

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void Implicit_Error_to_Result_creates_single_error_failure()
    {
        Result result = Error.NotFound("Item", "Not found");

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].Code.ShouldBe("Item");
        result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
    }

    // ── Result<T> ────────────────────────────────────────────────

    [Fact]
    public void Value_on_failed_result_throws_InvalidOperationException()
    {
        var result = Result<int>.Failure(Error.Failure("Fail", "Something went wrong"));

        var ex = Should.Throw<InvalidOperationException>(() => _ = result.Value);
        ex.Message.ShouldContain("Value");
    }

    [Fact]
    public void Success_resultT_has_empty_errors_and_value()
    {
        var result = Result<string>.Success("hello");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Failure_resultT_with_multiple_errors()
    {
        var result = Result<int>.Failure(
            Error.Validation("A", "First"),
            Error.Validation("B", "Second"));

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void Failure_resultT_from_IEnumerable_errors()
    {
        IEnumerable<Error> errors = [Error.Failure("X", "err")];

        var result = Result<int>.Failure(errors);

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);
    }

    [Fact]
    public void Implicit_TValue_to_ResultT_creates_success()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Implicit_Error_to_ResultT_creates_failure()
    {
        Result<string> result = Error.Conflict("Dup", "Already exists");

        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].Type.ShouldBe(ErrorType.Conflict);
    }

    // ── Error ────────────────────────────────────────────────────

    [Fact]
    public void Error_None_has_empty_code_and_description()
    {
        var error = Error.None;

        error.Code.ShouldBe(string.Empty);
        error.Description.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    public void Error_factory_methods_set_correct_type(ErrorType expectedType)
    {
        var error = expectedType switch
        {
            ErrorType.Failure => Error.Failure("code", "desc"),
            ErrorType.Validation => Error.Validation("code", "desc"),
            ErrorType.NotFound => Error.NotFound("code", "desc"),
            ErrorType.Conflict => Error.Conflict("code", "desc"),
            ErrorType.Unauthorized => Error.Unauthorized("code", "desc"),
            ErrorType.Forbidden => Error.Forbidden("code", "desc"),
            _ => throw new ArgumentOutOfRangeException()
        };

        error.Type.ShouldBe(expectedType);
        error.Code.ShouldBe("code");
        error.Description.ShouldBe("desc");
    }

    // ── ValidationResult ─────────────────────────────────────────

    [Fact]
    public void ValidationResult_is_always_failure()
    {
        var result = ValidationResult.WithErrors(Error.Validation("X", "Bad"));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeAssignableTo<Result>();
    }

    [Fact]
    public void ValidationResultT_is_always_failure()
    {
        var result = ValidationResult<int>.WithErrors(Error.Validation("X", "Bad"));

        result.IsFailure.ShouldBeTrue();
        result.ShouldBeAssignableTo<Result<int>>();
    }
}
