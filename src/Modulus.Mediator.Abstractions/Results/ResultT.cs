namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents the outcome of an operation that returns a value of type <typeparamref name="TValue"/>.
/// </summary>
/// <typeparam name="TValue">The type of the value produced on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value)
        : base(true, [])
    {
        _value = value;
    }

    /// <summary>
    /// Initializes a failed <see cref="Result{TValue}"/>.
    /// </summary>
    protected Result(Error[] errors)
        : base(false, errors)
    {
    }

    /// <summary>
    /// Gets the value of the result.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing <see cref="Value"/> on a failed result.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    /// <summary>Creates a successful result with the specified value.</summary>
    public static Result<TValue> Success(TValue value) => new(value);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public new static Result<TValue> Failure(params Error[] errors) => new(errors);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public new static Result<TValue> Failure(IEnumerable<Error> errors) => new(errors.ToArray());

    /// <summary>Applies one of two functions depending on whether the result is a success or failure.</summary>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Result<TValue>, TOut> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(this);

    /// <summary>Implicitly converts a value to a successful <see cref="Result{TValue}"/>.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Implicitly converts an <see cref="Error"/> to a failed <see cref="Result{TValue}"/>.</summary>
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
