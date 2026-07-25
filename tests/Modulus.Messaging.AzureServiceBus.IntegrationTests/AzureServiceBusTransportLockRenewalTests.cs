using Shouldly;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests;

// Plain [Fact]s, deliberately without [Trait("Category", "Integration")]: this is a pure
// arithmetic computation with no broker dependency, so it must run in the default (non-Docker)
// test filter. AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration is internal, visible
// here via InternalsVisibleTo on Modulus.Messaging.AzureServiceBus.
public sealed class AzureServiceBusTransportLockRenewalTests
{
    [Fact]
    public void ComputeMaxAutoLockRenewalDuration_DefaultRetryPolicy_IsWorstCaseRetryBudgetPlusSafetyMargin()
    {
        var policy = new RetryPolicyOptions();

        var result = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(policy);

        // Worst-case sleep budget for the default policy across 4 delays (attempts 1-4 of 5):
        // 1s + 6s + 16s + 30s(capped) = 53s, plus the fixed 2-minute safety margin = 173s.
        result.ShouldBe(TimeSpan.FromSeconds(173));
    }

    [Fact]
    public void ComputeMaxAutoLockRenewalDuration_SingleAttempt_NoRetryDelayContributesToTheBudget()
    {
        var policy = new RetryPolicyOptions { MaxAttempts = 1 };

        var result = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(policy);

        // No delay is ever awaited with a single attempt, but the floor still applies.
        result.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ComputeMaxAutoLockRenewalDuration_ZeroOrNegativeMaxAttempts_ClampsToOneAttempt()
    {
        var zero = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(new RetryPolicyOptions { MaxAttempts = 0 });
        var negative = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(new RetryPolicyOptions { MaxAttempts = -5 });
        var one = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(new RetryPolicyOptions { MaxAttempts = 1 });

        zero.ShouldBe(one);
        negative.ShouldBe(one);
    }

    [Fact]
    public void ComputeMaxAutoLockRenewalDuration_LargerRetryBudget_ScalesUpBeyondTheHardcodedFiveMinutes()
    {
        var aggressive = new RetryPolicyOptions
        {
            MaxAttempts = 10,
            InitialInterval = TimeSpan.FromMinutes(1),
            MaxInterval = TimeSpan.FromMinutes(10),
            IntervalIncrement = TimeSpan.FromMinutes(1),
        };

        var result = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(aggressive);

        // The previous hardcoded window (5 minutes) could not possibly outlive this retry
        // budget; the computed value must scale with the configured policy instead.
        result.ShouldBeGreaterThan(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ComputeMaxAutoLockRenewalDuration_SmallRetryBudget_StillIncludesTheSafetyMargin()
    {
        var noWait = new RetryPolicyOptions
        {
            MaxAttempts = 2,
            InitialInterval = TimeSpan.Zero,
            MaxInterval = TimeSpan.Zero,
            IntervalIncrement = TimeSpan.Zero,
        };

        var result = AzureServiceBusTransport.ComputeMaxAutoLockRenewalDuration(noWait);

        // Zero-delay retry budget: the result is entirely the floor/safety margin, but it must
        // still be well above zero to cover real handler execution time.
        result.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }
}
