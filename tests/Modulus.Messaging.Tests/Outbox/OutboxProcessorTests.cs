using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// Drives the OutboxProcessor BackgroundService poll loop directly against a fake
// IOutboxDispatcher — no database, no transport, no real broker.
public sealed class OutboxProcessorTests
{
    private static MessagingOptions FastPollOptions() => new()
    {
        OutboxPollInterval = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public async Task ExecuteAsync_Running_InvokesDispatcherRepeatedly()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var processor = new OutboxProcessor(dispatcher, NullLogger<OutboxProcessor>.Instance, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(10),
                because: "the poll loop should invoke the dispatcher repeatedly");
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
        var processor = new OutboxProcessor(dispatcher, NullLogger<OutboxProcessor>.Instance, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.WaitForConditionAsync(
                () => dispatcher.CallCount >= 2,
                timeout: TimeSpan.FromSeconds(10),
                because: "a dispatcher exception must not kill the poll loop");
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
        var processor = new OutboxProcessor(dispatcher, NullLogger<OutboxProcessor>.Instance, FastPollOptions());

        await processor.StartAsync(CancellationToken.None);
        await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

        var stopTask = processor.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.ShouldBe(stopTask);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsLoopPromptly()
    {
        var dispatcher = new FakeOutboxDispatcher();
        var processor = new OutboxProcessor(dispatcher, NullLogger<OutboxProcessor>.Instance, FastPollOptions());
        using var cts = new CancellationTokenSource();

        await processor.StartAsync(cts.Token);
        await TestWait.WaitForConditionAsync(() => dispatcher.CallCount >= 1);

        cts.Cancel();
        var stopTask = processor.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.ShouldBe(stopTask);
    }
}
