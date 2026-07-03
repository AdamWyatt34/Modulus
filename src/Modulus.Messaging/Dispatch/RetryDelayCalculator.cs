namespace Modulus.Messaging.Dispatch;

/// <summary>
/// Computes exponential backoff delays from <see cref="RetryPolicyOptions"/>:
/// <c>delay(n) = min(MaxInterval, InitialInterval + IntervalIncrement * (2^(n-1) - 1))</c>
/// for retry number <c>n</c> (1-based). Approximates, but is not bit-identical to, the
/// MassTransit exponential policy previously in use.
/// </summary>
internal static class RetryDelayCalculator
{
    public static TimeSpan GetDelay(RetryPolicyOptions policy, int retryNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);

        // Compute in double to avoid TimeSpan overflow for large retry numbers;
        // the exponent is capped because 2^30 increments already exceeds any sane MaxInterval.
        var exponent = Math.Min(retryNumber - 1, 30);
        var incrementMs = policy.IntervalIncrement.TotalMilliseconds * (Math.Pow(2, exponent) - 1);
        var delayMs = policy.InitialInterval.TotalMilliseconds + incrementMs;

        return delayMs >= policy.MaxInterval.TotalMilliseconds
            ? policy.MaxInterval
            : TimeSpan.FromMilliseconds(delayMs);
    }
}
