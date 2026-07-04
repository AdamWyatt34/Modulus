using Modulus.Messaging.Outbox;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Records Notify calls; WaitAsync returns a settable result immediately.</summary>
public sealed class FakeOutboxNotifier : IOutboxNotifier
{
    private int _notifyCount;

    public int NotifyCount => Volatile.Read(ref _notifyCount);

    public bool NextWaitResult { get; set; }

    public void Notify() => Interlocked.Increment(ref _notifyCount);

    public Task<bool> WaitAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        => Task.FromResult(NextWaitResult);
}
