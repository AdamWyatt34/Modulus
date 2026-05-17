using System.Reflection;
using System.Reflection.Emit;
using Modulus.Messaging.Internals;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Internals;

public class AssemblyExtensionsTests
{
    [Fact]
    public void GetTypesSafe_DynamicAssembly_ReturnsEmpty()
    {
        var name = new AssemblyName($"DynamicTest_{Guid.NewGuid():N}");
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

        var types = dynamicAssembly.GetTypesSafe();

        types.ShouldBeEmpty();
    }

    [Fact]
    public void GetTypesSafe_NormalAssembly_ReturnsAllTypes()
    {
        var assembly = typeof(AssemblyExtensionsTests).Assembly;

        var types = assembly.GetTypesSafe();

        types.ShouldNotBeEmpty();
        types.ShouldContain(t => t == typeof(AssemblyExtensionsTests));
    }
}
