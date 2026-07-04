using Modulus.Messaging.Outbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

public sealed class OutboxNotifierTests
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WaitAsync_NotifyBeforeWait_CompletesImmediately()
    {
        var notifier = new OutboxNotifier();

        notifier.Notify();
        var signaled = await notifier.WaitAsync(LongWait);

        signaled.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitAsync_NoNotify_ReturnsFalseAfterTimeout()
    {
        var notifier = new OutboxNotifier();

        var signaled = await notifier.WaitAsync(ShortWait);

        signaled.ShouldBeFalse();
    }

    [Fact]
    public async Task WaitAsync_NotifyWhileWaiting_ReturnsTrue()
    {
        var notifier = new OutboxNotifier();

        var waitTask = notifier.WaitAsync(LongWait);
        notifier.Notify();

        (await waitTask).ShouldBeTrue();
    }

    [Fact]
    public async Task Notify_ManyCallsCoalesce_SingleWake()
    {
        var notifier = new OutboxNotifier();

        for (var i = 0; i < 5; i++)
            notifier.Notify();

        (await notifier.WaitAsync(LongWait)).ShouldBeTrue();
        (await notifier.WaitAsync(ShortWait)).ShouldBeFalse();
    }

    [Fact]
    public async Task WaitAsync_AfterConsumedWake_ResetsAutomatically()
    {
        var notifier = new OutboxNotifier();

        notifier.Notify();
        (await notifier.WaitAsync(LongWait)).ShouldBeTrue();

        notifier.Notify();
        (await notifier.WaitAsync(LongWait)).ShouldBeTrue();
        (await notifier.WaitAsync(ShortWait)).ShouldBeFalse();
    }

    [Fact]
    public async Task WaitAsync_Cancelled_ThrowsPromptly()
    {
        var notifier = new OutboxNotifier();
        using var cts = new CancellationTokenSource();

        var waitTask = notifier.WaitAsync(LongWait, cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(waitTask);
    }

    [Fact]
    public async Task Notify_ParallelStorm_NeverThrowsAndLeavesAtMostOneWake()
    {
        var notifier = new OutboxNotifier();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1_000; i++)
                notifier.Notify();
        })));

        (await notifier.WaitAsync(LongWait)).ShouldBeTrue();
        (await notifier.WaitAsync(ShortWait)).ShouldBeFalse();
    }
}
