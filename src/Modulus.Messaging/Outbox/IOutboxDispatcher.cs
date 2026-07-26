namespace Modulus.Messaging.Outbox;

/// <summary>
/// Executes a single dispatch pass over pending outbox messages: fetch, deserialize,
/// publish, and mark processed or failed. Extracted from the polling loop so the
/// dispatch logic can run (and be tested) without a <c>BackgroundService</c> lifetime.
/// </summary>
internal interface IOutboxDispatcher
{
    /// <summary>
    /// Returns the number of messages that made forward progress this pass: published, or
    /// durably marked failed (attempt recorded, claim released, and backed off, so it will not
    /// be reclaimed until that backoff elapses). A message whose own MarkAsFailed bookkeeping
    /// call fails does not count — it is unchanged in the store and stays claimed by this
    /// dispatcher until its claim lease naturally expires, at which point it becomes reclaimable
    /// again (by this instance or any other). A full batch of progress signals probable backlog
    /// so the caller can re-dispatch immediately instead of waiting.
    /// </summary>
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default);
}
