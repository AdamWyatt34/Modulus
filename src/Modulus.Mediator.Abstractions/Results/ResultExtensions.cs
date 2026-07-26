namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Combinators over <see cref="Task{TResult}"/>-wrapped results so railway-oriented chains
/// compose without awaiting every intermediate step:
/// <c>await LoadOrder(id).Ensure(o =&gt; o.IsOpen, error).Bind(o =&gt; Ship(o)).Map(o =&gt; o.Id)</c>.
/// </summary>
public static class ResultExtensions
{
    // ── Task<Result> sources ─────────────────────────────────────

    /// <summary>Chains the next operation when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result> Bind(this Task<Result> resultTask, Func<Result> next)
        => (await resultTask.ConfigureAwait(false)).Bind(next);

    /// <summary>Chains the next value-producing operation when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Bind<TOut>(this Task<Result> resultTask, Func<Result<TOut>> next)
        => (await resultTask.ConfigureAwait(false)).Bind(next);

    /// <summary>Chains the next asynchronous operation when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result> Bind(this Task<Result> resultTask, Func<Task<Result>> next)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);

    /// <summary>Chains the next asynchronous value-producing operation when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Bind<TOut>(this Task<Result> resultTask, Func<Task<Result<TOut>>> next)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);

    /// <summary>Produces a value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Map<TOut>(this Task<Result> resultTask, Func<TOut> map)
        => (await resultTask.ConfigureAwait(false)).Map(map);

    /// <summary>Produces a value asynchronously when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Map<TOut>(this Task<Result> resultTask, Func<Task<TOut>> map)
        => await (await resultTask.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

    /// <summary>Executes a side effect when the awaited result is a success, then returns the result unchanged.</summary>
    public static async Task<Result> Tap(this Task<Result> resultTask, Action action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    /// <summary>Executes an asynchronous side effect when the awaited result is a success, then returns the result unchanged.</summary>
    public static async Task<Result> Tap(this Task<Result> resultTask, Func<Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

    /// <summary>Fails a successful awaited result with <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    public static async Task<Result> Ensure(this Task<Result> resultTask, Func<bool> predicate, Error error)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);

    /// <summary>Applies one of two functions depending on whether the awaited result is a success or failure.</summary>
    public static async Task<TOut> Match<TOut>(this Task<Result> resultTask, Func<TOut> onSuccess, Func<Result, TOut> onFailure)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);

    /// <summary>Applies one of two asynchronous functions depending on whether the awaited result is a success or failure.</summary>
    public static async Task<TOut> Match<TOut>(this Task<Result> resultTask, Func<Task<TOut>> onSuccess, Func<Result, Task<TOut>> onFailure)
        => await (await resultTask.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);

    // ── Task<Result<TValue>> sources ─────────────────────────────

    /// <summary>Chains the next operation on the value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Bind<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, Result<TOut>> next)
        => (await resultTask.ConfigureAwait(false)).Bind(next);

    /// <summary>Chains the next valueless operation on the value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result> Bind<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Result> next)
        => (await resultTask.ConfigureAwait(false)).Bind(next);

    /// <summary>Chains the next asynchronous operation on the value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Bind<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, Task<Result<TOut>>> next)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);

    /// <summary>Chains the next asynchronous valueless operation on the value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result> Bind<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Task<Result>> next)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);

    /// <summary>Transforms the value when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Map<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, TOut> map)
        => (await resultTask.ConfigureAwait(false)).Map(map);

    /// <summary>Transforms the value asynchronously when the awaited result is a success; otherwise propagates the failure.</summary>
    public static async Task<Result<TOut>> Map<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, Task<TOut>> map)
        => await (await resultTask.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

    /// <summary>Executes a side effect on the value when the awaited result is a success, then returns the result unchanged.</summary>
    public static async Task<Result<TValue>> Tap<TValue>(this Task<Result<TValue>> resultTask, Action<TValue> action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    /// <summary>Executes an asynchronous side effect on the value when the awaited result is a success, then returns the result unchanged.</summary>
    public static async Task<Result<TValue>> Tap<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

    /// <summary>Fails a successful awaited result with <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/> for the value.</summary>
    public static async Task<Result<TValue>> Ensure<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, bool> predicate, Error error)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);

    /// <summary>Fails a successful awaited result when <paramref name="predicate"/> returns <see langword="false"/> for the value, building the error from the value.</summary>
    public static async Task<Result<TValue>> Ensure<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, errorFactory);

    /// <summary>Applies one of two functions depending on whether the awaited result is a success or failure.</summary>
    public static async Task<TOut> Match<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, TOut> onSuccess, Func<Result<TValue>, TOut> onFailure)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);

    /// <summary>Applies one of two asynchronous functions depending on whether the awaited result is a success or failure.</summary>
    public static async Task<TOut> Match<TValue, TOut>(this Task<Result<TValue>> resultTask, Func<TValue, Task<TOut>> onSuccess, Func<Result<TValue>, Task<TOut>> onFailure)
        => await (await resultTask.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
}
