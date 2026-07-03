using Shouldly;
using Xunit;
using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Tests.Unit.Domain;

public class OrderTests
{
    [Fact]
    public void Create_should_set_id()
    {
        var id = Guid.NewGuid();

        var entity = Order.Create(id, "test", 1.0m);

        entity.Id.ShouldBe(id);
    }

    [Fact]
    public void Create_should_set_properties()
    {
        var id = Guid.NewGuid();

        var entity = Order.Create(id, "test", 1.0m);

        entity.CustomerName.ShouldBe("test");
        entity.Total.ShouldBe(1.0m);
    }
}
