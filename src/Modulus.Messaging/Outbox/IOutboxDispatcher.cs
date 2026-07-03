namespace Modulus.Messaging.Outbox;

/// <summary>
/// Executes a single dispatch pass over pending outbox messages: fetch, deserialize,
/// publish, and mark processed or failed. Extracted from the polling loop so the
/// dispatch logic can run (and be tested) without a <c>BackgroundService</c> lifetime.
/// </summary>
internal interface IOutboxDispatcher
{
    Task DispatchPendingAsync(CancellationToken cancellationToken = default);
}
