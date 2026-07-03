using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests.Generators;

public class ConsumerGeneratorTests
{
    private static ConsumerOptions CreateOptions() => new()
    {
        EventName = "OrderShipped",
        EventNamespace = "EShop.Orders.Integration.IntegrationEvents",
        ModuleName = "Shipping",
        SolutionName = "EShop",
    };

    [Fact]
    public void Generate_ReturnsOutputAtIntegrationEventHandlersPath()
    {
        var generator = new ConsumerGenerator();

        var output = generator.Generate(CreateOptions());

        output.RelativePath.ShouldBe("src/Shipping.Infrastructure/IntegrationEventHandlers/OrderShippedHandler.cs");
    }

    [Fact]
    public void Generate_ImplementsIIntegrationEventHandler()
    {
        var generator = new ConsumerGenerator();

        var output = generator.Generate(CreateOptions());

        output.Content.ShouldContain("namespace EShop.Shipping.Infrastructure.IntegrationEventHandlers;");
        output.Content.ShouldContain("public sealed class OrderShippedHandler : IIntegrationEventHandler<OrderShipped>");
        output.Content.ShouldContain("public Task Handle(OrderShipped @event, CancellationToken cancellationToken = default)");
    }

    [Fact]
    public void Generate_ImportsEventNamespace()
    {
        var generator = new ConsumerGenerator();

        var output = generator.Generate(CreateOptions());

        output.Content.ShouldContain("using EShop.Orders.Integration.IntegrationEvents;");
        output.Content.ShouldContain("using Modulus.Messaging.Abstractions;");
    }

    [Fact]
    public void Generate_HandlerBodyReturnsCompletedTask()
    {
        var generator = new ConsumerGenerator();

        var output = generator.Generate(CreateOptions());

        output.Content.ShouldContain("return Task.CompletedTask;");
    }
}
