namespace Modulus.Messaging.Outbox;

/// <summary>
/// Default <see cref="IOutboxNotifier"/>: an async auto-reset event built on a
/// <see cref="SemaphoreSlim"/> capped at one — the cap gives coalescing (a burst of
/// notifies parks a single wake) and auto-reset (a successful wait consumes it).
/// </summary>
internal sealed class OutboxNotifier : IOutboxNotifier
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        // Benign race: between this check and Release, a waiter may consume the parked
        // count, dropping this notify — the affected row then waits at most one poll
        // interval (the fallback sweep), it is never lost. The check exists so coalesced
        // notifies under write bursts return here instead of throwing per call.
        if (_signal.CurrentCount != 0)
            return;

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Raced with a concurrent Notify — a wake is already pending; coalesced.
        }
    }

    public Task<bool> WaitAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        => _signal.WaitAsync(maxWait, cancellationToken);
}
