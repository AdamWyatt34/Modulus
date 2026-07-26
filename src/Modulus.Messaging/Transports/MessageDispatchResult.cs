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

    /// <summary>
    /// The attempt failed with attempts remaining and broker-native redelivery is enabled
    /// (<see cref="MessagingOptions.ConsumerRetryMode"/>): the transport should schedule a
    /// delayed redelivery of the message (with its incremented attempt header) and consume
    /// the original — freeing the concurrency slot an in-process retry sleep would pin.
    /// Only returned when <see cref="ConsumerRetryMode.Broker"/> is configured; transports
    /// without a native delay mechanism should treat it as a redelivery request (requeue).
    /// </summary>
    Retry,
}
