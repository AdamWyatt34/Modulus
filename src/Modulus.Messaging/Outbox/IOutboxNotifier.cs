namespace Modulus.Messaging.Outbox;

/// <summary>
/// Wake signal for the outbox processor. Signal sources call <see cref="Notify"/> when new
/// outbox rows become visible (committed), so pending messages are dispatched immediately
/// instead of waiting out the poll interval. Registered as a singleton; the polling sweep
/// remains as the fallback for wake sources that cannot signal (other process instances,
/// external writers, externally-owned transactions).
/// </summary>
/// <remarks>
/// This is also the extension point for external change-notification listeners: a hosted
/// service watching the database (for example PostgreSQL <c>LISTEN/NOTIFY</c>) injects the
/// singleton and calls <see cref="Notify"/> — no further integration is required.
/// </remarks>
public interface IOutboxNotifier
{
    /// <summary>
    /// Signals that outbox rows may be pending. Coalescing: any number of calls while a wake
    /// is already pending results in a single wake. Never blocks and never throws.
    /// </summary>
    void Notify();

    /// <summary>
    /// Waits until notified or until <paramref name="maxWait"/> elapses. Returns
    /// <see langword="true"/> when woken by a notification, <see langword="false"/> on timeout.
    /// A notification that arrived before the wait began completes it immediately
    /// (auto-reset event semantics).
    /// </summary>
    Task<bool> WaitAsync(TimeSpan maxWait, CancellationToken cancellationToken = default);
}
