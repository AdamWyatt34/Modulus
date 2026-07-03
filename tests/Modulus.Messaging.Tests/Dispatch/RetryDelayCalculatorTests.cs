using Modulus.Messaging.Dispatch;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Dispatch;

public class RetryDelayCalculatorTests
{
    private static RetryPolicyOptions Policy(
        int initialSeconds = 1, int incrementSeconds = 5, int maxSeconds = 30) => new()
        {
            InitialInterval = TimeSpan.FromSeconds(initialSeconds),
            IntervalIncrement = TimeSpan.FromSeconds(incrementSeconds),
            MaxInterval = TimeSpan.FromSeconds(maxSeconds),
        };

    [Fact]
    public void GetDelay_FirstRetry_ReturnsInitialInterval()
    {
        RetryDelayCalculator.GetDelay(Policy(), 1).ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetDelay_GrowsExponentially_UntilMaxInterval()
    {
        // delay(n) = initial + increment * (2^(n-1) - 1)
        RetryDelayCalculator.GetDelay(Policy(), 2).ShouldBe(TimeSpan.FromSeconds(6));   // 1 + 5*1
        RetryDelayCalculator.GetDelay(Policy(), 3).ShouldBe(TimeSpan.FromSeconds(16));  // 1 + 5*3
        RetryDelayCalculator.GetDelay(Policy(), 4).ShouldBe(TimeSpan.FromSeconds(30));  // 1 + 5*7 = 36 → clamped
    }

    [Fact]
    public void GetDelay_LargeRetryNumber_ClampsToMaxIntervalWithoutOverflow()
    {
        RetryDelayCalculator.GetDelay(Policy(), 1000).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GetDelay_ZeroIntervals_ReturnsZero()
    {
        var policy = Policy(initialSeconds: 0, incrementSeconds: 0, maxSeconds: 0);
        RetryDelayCalculator.GetDelay(policy, 3).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void GetDelay_RetryNumberBelowOne_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RetryDelayCalculator.GetDelay(Policy(), 0));
    }
}
