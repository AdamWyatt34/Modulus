using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

public class TestWaitTests
{
    [Fact]
    public async Task WaitForConditionAsync_ConditionBecomesTrue_ReturnsAsSoonAsItDoes()
    {
        var flipAt = DateTime.UtcNow.AddMilliseconds(100);

        await TestWait.WaitForConditionAsync(() => DateTime.UtcNow >= flipAt);
    }

    [Fact]
    public async Task WaitForConditionAsync_ConditionNeverTrue_ThrowsTimeoutExceptionWithBecause()
    {
        var exception = await Should.ThrowAsync<TimeoutException>(() =>
            TestWait.WaitForConditionAsync(
                () => false,
                timeout: TimeSpan.FromMilliseconds(100),
                because: "the projection handler never ran"));

        exception.Message.ShouldContain("0.1s");
        exception.Message.ShouldContain("the projection handler never ran");
    }

    [Fact]
    public async Task WaitForConditionAsync_WithoutBecause_OmitsTheColonSuffix()
    {
        var exception = await Should.ThrowAsync<TimeoutException>(() =>
            TestWait.WaitForConditionAsync(() => false, timeout: TimeSpan.FromMilliseconds(50)));

        exception.Message.ShouldNotContain(":");
    }

    [Fact]
    public async Task WaitForConditionAsync_AsyncConditionOverload_PollsUntilTrue()
    {
        var counter = 0;

        await TestWait.WaitForConditionAsync(async () =>
        {
            await Task.Yield();
            counter++;
            return counter >= 3;
        });

        counter.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task WaitForConditionAsync_AsyncConditionNeverTrue_ThrowsTimeoutException()
    {
        await Should.ThrowAsync<TimeoutException>(() =>
            TestWait.WaitForConditionAsync(
                () => Task.FromResult(false),
                timeout: TimeSpan.FromMilliseconds(75)));
    }
}
