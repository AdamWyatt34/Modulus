namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>
/// Polls a condition instead of sleeping a fixed interval, so tests pass as soon as
/// the condition holds and fail with a clear message when it never does.
/// </summary>
public static class TestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    public static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        string? because = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Condition not met within {effectiveTimeout.TotalSeconds:0.#}s{(because is null ? "" : $": {because}")}");

            await Task.Delay(PollInterval);
        }
    }
}
