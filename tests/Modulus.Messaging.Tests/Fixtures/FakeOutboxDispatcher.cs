using Modulus.Messaging.Outbox;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Records DispatchPendingAsync calls; can be told to throw on the next call(s).</summary>
public sealed class FakeOutboxDispatcher : IOutboxDispatcher
{
    private readonly Lock _sync = new();
    private readonly Queue<int> _results = new();
    private int _throwCount;
    private Exception? _exception;

    public int CallCount { get; private set; }

    /// <summary>Queues fetched-count results for upcoming calls; once drained, calls return 0.</summary>
    public void EnqueueResults(params int[] fetchedCounts)
    {
        lock (_sync)
        {
            foreach (var count in fetchedCounts)
                _results.Enqueue(count);
        }
    }

    /// <summary>Configures the next <paramref name="times"/> calls to throw <paramref name="exception"/> before resuming normal behavior.</summary>
    public void ThrowOnNextCall(Exception exception, int times = 1)
    {
        lock (_sync)
        {
            _exception = exception;
            _throwCount = times;
        }
    }

    public Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            CallCount++;

            if (_throwCount > 0)
            {
                _throwCount--;
                throw _exception!;
            }

            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : 0);
        }
    }
}
