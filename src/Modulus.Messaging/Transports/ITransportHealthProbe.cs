namespace Modulus.Messaging.Transports;

/// <summary>
/// Optional health introspection for an <see cref="IMessageTransport"/>. Transports that
/// implement it are probed by the <c>AddModulusMessaging()</c> health check; transports that
/// don't are reported healthy with a note. Kept separate from <see cref="IMessageTransport"/>
/// so custom transports opt in rather than break.
/// </summary>
public interface ITransportHealthProbe
{
    /// <summary>Checks broker connectivity. Implementations may establish the connection to do so.</summary>
    ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a transport health probe.</summary>
/// <param name="IsHealthy">Whether the transport considers itself able to publish and consume.</param>
/// <param name="Description">Optional human-readable detail surfaced in the health report.</param>
public readonly record struct TransportHealth(bool IsHealthy, string? Description = null);
