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
        : this(EnumerateIntegrationEventTypes(assemblies))
    {
    }

    /// <summary>
    /// Test seam: registers an already-filtered sequence of candidate types directly,
    /// bypassing assembly scanning. A static method rather than a second public constructor
    /// overload, deliberately — an <c>IEnumerable&lt;Assembly&gt;</c> overload and an
    /// <c>IEnumerable&lt;Type&gt;</c> overload both accept an empty collection expression
    /// (<c>[]</c>) with nothing to disambiguate on, which would make every existing
    /// <c>new MessageTypeRegistry([])</c> call site ambiguous.
    /// </summary>
    internal static MessageTypeRegistry ForTypes(IEnumerable<Type> candidateTypes) => new(candidateTypes);

    private MessageTypeRegistry(IEnumerable<Type> candidateTypes)
    {
        _typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        _namesByType = [];

        foreach (var type in candidateTypes)
        {
            var name = GetStableName(type);

            if (_typesByName.TryGetValue(name, out var existing))
            {
                // The exact same Type scanned twice (e.g. a duplicate assembly entry) is a
                // harmless no-op; two distinct types colliding on the same wire name is a
                // genuine, silent-data-loss configuration error and must not first-win.
                if (ReferenceEquals(existing, type))
                    continue;

                throw new InvalidOperationException(
                    $"Integration event types '{existing.AssemblyQualifiedName}' and " +
                    $"'{type.AssemblyQualifiedName}' both resolve to the wire name '{name}'. " +
                    "Integration event type names (Type.FullName) must be unique across every " +
                    "assembly registered in MessagingOptions.Assemblies.");
            }

            _typesByName.Add(name, type);
            _namesByType.TryAdd(type, name);
        }
    }

    private static IEnumerable<Type> EnumerateIntegrationEventTypes(IEnumerable<Assembly> assemblies)
    {
        var integrationEventType = typeof(IIntegrationEvent);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypesSafe())
            {
                if (type is { IsAbstract: false, IsInterface: false }
                    && integrationEventType.IsAssignableFrom(type))
                {
                    yield return type;
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
