using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// Drives the OutboxProcessor BackgroundService loop directly against a fake
// IOutboxDispatcher — no database, no transport, no real broker.
public sealed class OutboxProcessorTests
{
    private static MessagingOptions FastPollOptions() => new()
    {
        OutboxPollInterval = TimeSpan.FromSeconds(1),
    };

    // Long enough that any dispatch observed after the first can only have been
    // caused by a wake signal or backlog drain, never by the poll fallback.
    private static MessagingOptions SlowPollOptions() => new()
    {
        OutboxPollInterval = TimeSpan.FromSeconds(30),
    };

    private static OutboxProcessor CreateProcessor(
        IOutboxDispatcher dispatcher,
        MessagingOptions options,
        IOutboxNotifier? notifier = null)
        => new(
            dispatcher,
            notifier ?? new OutboxNotifier(),
            NullLogger<OutboxProcessor>.Instance,
            options,
            new MessagingMetrics(meterFactory: null));

    /// <summary>Blocks the first dispatch until released, so a notify can arrive mid-dispatch.</summary>
    private sealed class BlockingDispatcher : IOutboxDispatcher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public void Release() => _release.TrySetResult();

        public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
                await _release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task ExecuteAsync_Running_InvokesDispatcherRepeatedly()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var processor = CreateProcessor(dispatcher, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(10),
                because: "the poll fallback should invoke the dispatcher repeatedly");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DispatcherThrowsOnce_LoopContinuesAndDispatchesAgain()
    {
        var dispatcher = new FakeOutboxDispatcher();
        dispatcher.ThrowOnNextCall(new InvalidOperationException("transient failure"));
        var processor = CreateProcessor(dispatcher, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(10),
                because: "a dispatcher exception must not kill the loop");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        dispatcher.CallCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task StopAsync_WhileRunning_StopsLoopPromptly()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var processor = CreateProcessor(dispatcher, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

        // Generous ceiling: shared CI runners under parallel load can deschedule the loop
        // for whole seconds; the assertion is "stops without waiting out a poll backlog",
        // not "stops within N milliseconds".
        var stopTask = processor.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(15)));

        completed.ShouldBe(stopTask);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsLoopPromptly()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var processor = CreateProcessor(dispatcher, FastPollOptions());
        using var cts = new CancellationTokenSource();

        await processor.StartAsync(cts.Token);
        await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

        cts.Cancel();
        // Generous ceiling: shared CI runners under parallel load can deschedule the loop
        // for whole seconds; the assertion is "stops without waiting out a poll backlog",
        // not "stops within N milliseconds".
        var stopTask = processor.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(15)));

        completed.ShouldBe(stopTask);
    }

    [Fact]
    public async Task ExecuteAsync_Notified_DispatchesWithoutWaitingForPollInterval()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var notifier = new OutboxNotifier();
        var processor = CreateProcessor(dispatcher, SlowPollOptions(), notifier);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

            notifier.Notify();

            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(5),
                because: "a notify must wake the processor long before the 30s poll interval");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FullBatchFetched_DrainsWithoutWaiting()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var options = SlowPollOptions();
        // Three full batches queued: the processor must re-dispatch immediately after each
        // and only then fall back to waiting (the fourth call returns 0).
        dispatcher.EnqueueResults(options.OutboxBatchSize, options.OutboxBatchSize, options.OutboxBatchSize);
        var processor = CreateProcessor(dispatcher, options);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 4,
                timeout: TimeSpan.FromSeconds(5),
                because: "full batches must drain back-to-back instead of waiting out the 30s poll interval");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NotifiedDuringDispatch_SignalIsNotLost()
    {
        var dispatcher = new BlockingDispatcher();
        var notifier = new OutboxNotifier();
        var processor = CreateProcessor(dispatcher, SlowPollOptions(), notifier);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

            // The processor is blocked inside dispatch — no waiter exists yet. The wake
            // must park and complete the wait that begins after the dispatch finishes.
            notifier.Notify();
            dispatcher.Release();

            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(5),
                because: "a notify arriving mid-dispatch must trigger the next pass promptly");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }
}
