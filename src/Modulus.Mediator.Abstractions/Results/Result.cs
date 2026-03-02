namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents the outcome of an operation that does not return a value.
/// </summary>
public class Result
{
    private readonly Error[] _errors;

    /// <summary>
    /// Initializes a new <see cref="Result"/>.
    /// </summary>
    protected Result(bool isSuccess, Error[] errors)
    {
        IsSuccess = isSuccess;
        _errors = errors;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the collection of errors. Empty when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public IReadOnlyList<Error> Errors => _errors;

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, []);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public static Result Failure(params Error[] errors) => new(false, errors);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public static Result Failure(IEnumerable<Error> errors) => new(false, errors.ToArray());

    /// <summary>Applies one of two functions depending on whether the result is a success or failure.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Result, TOut> onFailure)
        => IsSuccess ? onSuccess() : onFailure(this);

    /// <summary>Implicitly converts an <see cref="Error"/> to a failed <see cref="Result"/>.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}
