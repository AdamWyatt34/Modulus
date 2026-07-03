namespace Modulus.Messaging.Dispatch;

/// <summary>
/// Thrown when another delivery holds a live inbox reservation for a (message, handler)
/// pair. Deliberately an exception rather than a skip: acknowledging would lose the message
/// if the reservation's owner crashed, so the dispatch retries instead — if the owner
/// completes, the next attempt sees the pair processed; if the owner is gone, a later
/// attempt (or a dead-letter replay) takes the stale reservation over.
/// </summary>
internal sealed class InboxReservationPendingException(Guid messageId, string handlerName)
    : Exception($"Handler '{handlerName}' for message {messageId} is reserved by another in-flight delivery.");
