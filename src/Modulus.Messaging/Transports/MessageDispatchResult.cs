namespace Modulus.Messaging.Transports;

/// <summary>
/// The outcome the consumer pipeline reports back to the transport for a received message.
/// The pipeline owns in-process retries and idempotency; by the time a result is returned,
/// the transport only needs to acknowledge or dead-letter.
/// </summary>
public enum MessageDispatchResult
{
    /// <summary>The message was handled (or safely skipped) and must be acknowledged.</summary>
    Acknowledge,

    /// <summary>All processing attempts failed; the transport should dead-letter the message.</summary>
    DeadLetter,
}
