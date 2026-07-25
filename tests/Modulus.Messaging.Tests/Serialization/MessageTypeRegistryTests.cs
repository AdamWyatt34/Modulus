using System.Reflection;
using System.Reflection.Emit;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Serialization;

public class MessageTypeRegistryTests
{
    /// <summary>Builds a distinct, real (non-abstract, loadable) runtime Type with the exact
    /// given FullName, backed by its own dynamic assembly — so two calls with the same
    /// <paramref name="fullName"/> return genuinely different Type instances that collide on
    /// GetStableName, simulating two assemblies that each define a same-named event type.</summary>
    private static Type CreateTypeNamed(string fullName)
    {
        var assemblyName = new AssemblyName($"MessageTypeRegistryTests_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
        var typeBuilder = moduleBuilder.DefineType(fullName, TypeAttributes.Public);
        return typeBuilder.CreateType()!;
    }

    [Fact]
    public void ForTypes_DuplicateFullNameFromDifferentTypes_ThrowsNamingBothTypes()
    {
        // MessageTypeRegistry.cs:32-33 (pre-fix): duplicate FullName across assemblies
        // silently first-won instead of throwing.
        const string collidingName = "Modulus.Messaging.Tests.Serialization.DuplicateNamedEvent";
        var typeA = CreateTypeNamed(collidingName);
        var typeB = CreateTypeNamed(collidingName);

        var exception = Should.Throw<InvalidOperationException>(
            () => MessageTypeRegistry.ForTypes([typeA, typeB]));

        exception.Message.ShouldContain(typeA.AssemblyQualifiedName!);
        exception.Message.ShouldContain(typeB.AssemblyQualifiedName!);
    }

    [Fact]
    public void ForTypes_SameTypeRegisteredTwice_DoesNotThrow()
    {
        // The exact same Type appearing twice (e.g. from a duplicate assembly entry) is a
        // harmless no-op, not a collision — only two distinct types sharing a name must throw.
        var type = typeof(TestOrderCreatedEvent);

        Should.NotThrow(() => MessageTypeRegistry.ForTypes([type, type]));
    }

    [Fact]
    public void Ctor_SameAssemblyScannedTwice_DoesNotThrow()
    {
        // Defense in depth alongside the MessagingOptions.Assemblies dedup: even if a duplicate
        // assembly reaches this constructor directly, scanning it twice yields the same Type
        // instances both times, so this must not throw.
        var assembly = typeof(TestOrderCreatedEvent).Assembly;

        Should.NotThrow(() => new MessageTypeRegistry([assembly, assembly]));
    }

    [Fact]
    public void Resolve_RegisteredEventName_ReturnsType()
    {
        var registry = new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]);

        var resolved = registry.Resolve(typeof(TestOrderCreatedEvent).FullName!);

        resolved.ShouldBe(typeof(TestOrderCreatedEvent));
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var registry = new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]);

        registry.Resolve("Not.A.Registered.Type").ShouldBeNull();
    }

    [Fact]
    public void Resolve_AssemblyQualifiedName_ReturnsNull()
    {
        // Wire names are namespace-qualified only; assembly-qualified names must not resolve,
        // otherwise senders could smuggle assembly hints into type resolution.
        var registry = new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]);

        registry.Resolve(typeof(TestOrderCreatedEvent).AssemblyQualifiedName!).ShouldBeNull();
    }

    [Fact]
    public void GetName_RegisteredType_RoundTripsThroughResolve()
    {
        var registry = new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]);

        var name = registry.GetName(typeof(TestOrderCreatedEvent));

        registry.Resolve(name).ShouldBe(typeof(TestOrderCreatedEvent));
    }

    [Fact]
    public void GetName_UnregisteredType_ComputesStableName()
    {
        var registry = new MessageTypeRegistry([]);

        registry.GetName(typeof(TestOrderCreatedEvent))
            .ShouldBe(typeof(TestOrderCreatedEvent).FullName);
    }
}
