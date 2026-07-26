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

    /// <summary>Gets the first error of a failed result.</summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing <see cref="FirstError"/> on a successful result.</exception>
    public Error FirstError => IsFailure
        ? _errors[0]
        : throw new InvalidOperationException("Cannot access FirstError on a successful result.");

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, []);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public static Result Failure(params Error[] errors) => new(false, errors);

    /// <summary>Creates a failed result with the specified errors.</summary>
    public static Result Failure(IEnumerable<Error> errors) => new(false, errors.ToArray());

    /// <summary>Applies one of two functions depending on whether the result is a success or failure.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Result, TOut> onFailure)
        => IsSuccess ? onSuccess() : onFailure(this);

    /// <summary>Applies one of two asynchronous functions depending on whether the result is a success or failure.</summary>
    public Task<TOut> MatchAsync<TOut>(Func<Task<TOut>> onSuccess, Func<Result, Task<TOut>> onFailure)
        => IsSuccess ? onSuccess() : onFailure(this);

    /// <summary>Chains the next operation when this result is a success; otherwise propagates the failure.</summary>
    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : this;
    }

    /// <summary>Chains the next value-producing operation when this result is a success; otherwise propagates the failure.</summary>
    public Result<TOut> Bind<TOut>(Func<Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Result<TOut>.Failure(_errors);
    }

    /// <summary>Chains the next asynchronous operation when this result is a success; otherwise propagates the failure.</summary>
    public Task<Result> BindAsync(Func<Task<Result>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Task.FromResult(this);
    }

    /// <summary>Chains the next asynchronous value-producing operation when this result is a success; otherwise propagates the failure.</summary>
    public Task<Result<TOut>> BindAsync<TOut>(Func<Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Task.FromResult(Result<TOut>.Failure(_errors));
    }

    /// <summary>Produces a value when this result is a success; otherwise propagates the failure.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> returns <see langword="null"/>.</exception>
    public Result<TOut> Map<TOut>(Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Success(map()) : Result<TOut>.Failure(_errors);
    }

    /// <summary>Produces a value asynchronously when this result is a success; otherwise propagates the failure.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> returns <see langword="null"/>.</exception>
    public async Task<Result<TOut>> MapAsync<TOut>(Func<Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Success(await map().ConfigureAwait(false)) : Result<TOut>.Failure(_errors);
    }

    /// <summary>Executes a side effect when this result is a success, then returns the result unchanged.</summary>
    public Result Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess)
            action();
        return this;
    }

    /// <summary>Executes an asynchronous side effect when this result is a success, then returns the result unchanged.</summary>
    public async Task<Result> TapAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess)
            await action().ConfigureAwait(false);
        return this;
    }

    /// <summary>Fails a successful result with <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    public Result Ensure(Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (IsFailure)
            return this;
        return predicate() ? this : Failure(error);
    }

    /// <summary>Implicitly converts an <see cref="Error"/> to a failed <see cref="Result"/>.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}
