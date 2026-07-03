namespace Modulus.Messaging.Transports;

/// <summary>
/// Declares interest in one event type: the CLR type handlers are registered for
/// and the stable wire name the transport routes by.
/// </summary>
public sealed record TransportSubscription(Type EventType, string MessageTypeName);
