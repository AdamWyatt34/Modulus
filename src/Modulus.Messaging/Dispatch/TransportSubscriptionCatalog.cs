using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Dispatch;

/// <summary>
/// The set of event subscriptions this host consumes, computed once at registration time
/// from the handlers discovered in <see cref="MessagingOptions.Assemblies"/>.
/// Empty for publish-only hosts.
/// </summary>
internal sealed class TransportSubscriptionCatalog(IReadOnlyList<TransportSubscription> subscriptions)
{
    public IReadOnlyList<TransportSubscription> Subscriptions { get; } = subscriptions;
}
