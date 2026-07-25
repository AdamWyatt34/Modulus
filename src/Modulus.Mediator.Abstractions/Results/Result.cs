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
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="isSuccess"/> is <see langword="false"/> and <paramref name="errors"/> is empty.</exception>
    protected Result(bool isSuccess, Error[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (!isSuccess && errors.Length == 0)
            throw new ArgumentException("A failed result must have at least one error.", nameof(errors));

        IsSuccess = isSuccess;
        // Defensive copy: the caller's array must not be able to mutate this result after construction.
        _errors = (Error[])errors.Clone();
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
