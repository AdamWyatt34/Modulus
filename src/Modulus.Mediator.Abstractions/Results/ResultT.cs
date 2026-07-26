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
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Result<TValue> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    /// <summary>Creates a failed result with the specified errors.</summary>
    public new static Result<TValue> Failure(params Error[] errors) => new(errors);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public new static Result<TValue> Failure(IEnumerable<Error> errors) => new(errors.ToArray());

    /// <summary>Applies one of two functions depending on whether the result is a success or failure.</summary>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Result<TValue>, TOut> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(this);

    /// <summary>Applies one of two asynchronous functions depending on whether the result is a success or failure.</summary>
    public Task<TOut> MatchAsync<TOut>(Func<TValue, Task<TOut>> onSuccess, Func<Result<TValue>, Task<TOut>> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(this);

    /// <summary>Chains the next operation on the value when this result is a success; otherwise propagates the failure.</summary>
    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(Value) : Result<TOut>.Failure(Errors);
    }

    /// <summary>Chains the next valueless operation on the value when this result is a success; otherwise propagates the failure.</summary>
    public Result Bind(Func<TValue, Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(Value) : Result.Failure(Errors);
    }

    /// <summary>Chains the next asynchronous operation on the value when this result is a success; otherwise propagates the failure.</summary>
    public Task<Result<TOut>> BindAsync<TOut>(Func<TValue, Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(Value) : Task.FromResult(Result<TOut>.Failure(Errors));
    }

    /// <summary>Chains the next asynchronous valueless operation on the value when this result is a success; otherwise propagates the failure.</summary>
    public Task<Result> BindAsync(Func<TValue, Task<Result>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(Value) : Task.FromResult(Result.Failure(Errors));
    }

    /// <summary>Transforms the value when this result is a success; otherwise propagates the failure.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> returns <see langword="null"/>.</exception>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Success(map(Value)) : Result<TOut>.Failure(Errors);
    }

    /// <summary>Transforms the value asynchronously when this result is a success; otherwise propagates the failure.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> returns <see langword="null"/>.</exception>
    public async Task<Result<TOut>> MapAsync<TOut>(Func<TValue, Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Success(await map(Value).ConfigureAwait(false)) : Result<TOut>.Failure(Errors);
    }

    /// <summary>Executes a side effect on the value when this result is a success, then returns the result unchanged.</summary>
    public Result<TValue> Tap(Action<TValue> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess)
            action(Value);
        return this;
    }

    /// <summary>Executes an asynchronous side effect on the value when this result is a success, then returns the result unchanged.</summary>
    public async Task<Result<TValue>> TapAsync(Func<TValue, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess)
            await action(Value).ConfigureAwait(false);
        return this;
    }

    /// <summary>Fails a successful result with <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/> for the value.</summary>
    public Result<TValue> Ensure(Func<TValue, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (IsFailure)
            return this;
        return predicate(Value) ? this : Failure(error);
    }

    /// <summary>Fails a successful result when <paramref name="predicate"/> returns <see langword="false"/> for the value, building the error from the value.</summary>
    public Result<TValue> Ensure(Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);
        if (IsFailure)
            return this;
        return predicate(Value) ? this : Failure(errorFactory(Value));
    }

    /// <summary>Implicitly converts a value to a successful <see cref="Result{TValue}"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Implicitly converts an <see cref="Error"/> to a failed <see cref="Result{TValue}"/>.</summary>
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
