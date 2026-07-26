namespace Modulus.Testing;

/// <summary>
/// Polls a condition instead of sleeping a fixed interval, so a test passes as soon as the
/// condition holds and fails with a clear message when it never does — the pattern every
/// Modulus.Messaging test built against the in-memory transport uses to await asynchronous
/// delivery.
/// </summary>
public static class TestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Polls <paramref name="condition"/> every 25ms until it returns <see langword="true"/> or
    /// <paramref name="timeout"/> (default 5 seconds) elapses.
    /// </summary>
    /// <param name="condition">The synchronous condition to poll.</param>
    /// <param name="timeout">The maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="because">
    /// Optional context appended to the <see cref="TimeoutException"/> message if the condition
    /// never holds.
    /// </param>
    /// <exception cref="TimeoutException"><paramref name="condition"/> never returned <see langword="true"/> within the timeout.</exception>
    public static Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        string? because = null)
        => WaitForConditionAsync(() => Task.FromResult(condition()), timeout, because);

    /// <summary>
    /// Polls an asynchronous <paramref name="condition"/> every 25ms until it returns
    /// <see langword="true"/> or <paramref name="timeout"/> (default 5 seconds) elapses. Use this
    /// overload when the condition itself needs to await something — a database query through
    /// <c>OutboxTestQueries</c>/<c>InboxTestQueries</c>, for example.
    /// </summary>
    /// <param name="condition">The asynchronous condition to poll.</param>
    /// <param name="timeout">The maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="because">
    /// Optional context appended to the <see cref="TimeoutException"/> message if the condition
    /// never holds.
    /// </param>
    /// <exception cref="TimeoutException"><paramref name="condition"/> never returned <see langword="true"/> within the timeout.</exception>
    public static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        string? because = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (!await condition().ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Condition not met within {effectiveTimeout.TotalSeconds:0.#}s{(because is null ? "" : $": {because}")}");

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }
}
