using System.Reflection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Internals;

namespace Modulus.Messaging.Serialization;

/// <summary>
/// Maps stable wire names to CLR event types, built once from the configured assemblies.
/// Acts as the consumption allowlist: wire data never reaches <c>Type.GetType</c>, so a
/// malicious message type name cannot force arbitrary type resolution.
/// </summary>
internal sealed class MessageTypeRegistry
{
    private readonly Dictionary<string, Type> _typesByName;
    private readonly Dictionary<Type, string> _namesByType;

    public MessageTypeRegistry(IEnumerable<Assembly> assemblies)
    {
        _typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        _namesByType = [];

        var integrationEventType = typeof(IIntegrationEvent);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypesSafe())
            {
                if (type is { IsAbstract: false, IsInterface: false }
                    && integrationEventType.IsAssignableFrom(type))
                {
                    var name = GetStableName(type);
                    if (_typesByName.TryAdd(name, type))
                        _namesByType.TryAdd(type, name);
                }
            }
        }
    }

    /// <summary>The stable, assembly-neutral wire name for a message type.</summary>
    public static string GetStableName(Type type) => type.FullName ?? type.Name;

    /// <summary>Gets the wire name for a registered event type, or computes it for unregistered ones (publish side).</summary>
    public string GetName(Type type)
        => _namesByType.TryGetValue(type, out var name) ? name : GetStableName(type);

    /// <summary>Resolves a wire name to its registered event type, or <c>null</c> when unknown.</summary>
    public Type? Resolve(string messageTypeName)
        => _typesByName.TryGetValue(messageTypeName, out var type) ? type : null;
}
