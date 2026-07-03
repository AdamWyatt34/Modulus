using Modulus.Messaging.Outbox;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Records DispatchPendingAsync calls; can be told to throw on the next call(s).</summary>
public sealed class FakeOutboxDispatcher : IOutboxDispatcher
{
    private readonly Lock _sync = new();
    private int _throwCount;
    private Exception? _exception;

    public int CallCount { get; private set; }

    /// <summary>Configures the next <paramref name="times"/> calls to throw <paramref name="exception"/> before resuming normal behavior.</summary>
    public void ThrowOnNextCall(Exception exception, int times = 1)
    {
        lock (_sync)
        {
            _exception = exception;
            _throwCount = times;
        }
    }

    public Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            CallCount++;

            if (_throwCount > 0)
            {
                _throwCount--;
                throw _exception!;
            }
        }

        return Task.CompletedTask;
    }
}
