using System.Collections.Generic;
using System.Linq;
using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class EventGeneratorTests
{
    private static EventOptions CreateOptions(IReadOnlyList<EntityProperty>? properties = null) => new()
    {
        EventName = "OrderShipped",
        ModuleName = "Orders",
        SolutionName = "EShop",
        Properties = properties ?? [],
    };

    [Fact]
    public void Generate_ReturnsSingleOutputAtIntegrationEventsPath()
    {
        var generator = new EventGenerator();

        var outputs = generator.Generate(CreateOptions());

        outputs.Count.ShouldBe(1);
        outputs[0].RelativePath.ShouldBe("src/Orders.Integration/IntegrationEvents/OrderShipped.cs");
    }

    [Fact]
    public void Generate_NoProperties_ProducesParameterlessRecord()
    {
        var generator = new EventGenerator();

        var outputs = generator.Generate(CreateOptions());

        outputs[0].Content.ShouldContain("using Modulus.Messaging.Abstractions;");
        outputs[0].Content.ShouldContain("namespace EShop.Orders.Integration.IntegrationEvents;");
        outputs[0].Content.ShouldContain("public sealed record OrderShipped : IntegrationEvent;");
    }

    [Fact]
    public void Generate_WithProperties_ProducesPositionalRecord()
    {
        var generator = new EventGenerator();
        var properties = new List<EntityProperty> { new("OrderId", "Guid"), new("Total", "decimal") };

        var outputs = generator.Generate(CreateOptions(properties));

        outputs[0].Content.ShouldContain("public sealed record OrderShipped(Guid OrderId, decimal Total) : IntegrationEvent;");
    }
}
