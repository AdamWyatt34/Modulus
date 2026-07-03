using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Serialization;

public class MessageTypeRegistryTests
{
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
