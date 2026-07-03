using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;
using SampleApp.Orders.Application.Data;
using SampleApp.Orders.Application.Queries.GetOrder;
using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Tests.Unit.Queries;

public class GetOrderHandlerTests
{
    [Fact]
    public async Task Handle_should_return_order_when_it_exists()
    {
        var id = Guid.NewGuid();
        var queryDb = new FakeQueryDb(Order.Create(id, "Ada Lovelace", 42.50m));
        var handler = new GetOrderHandler(queryDb);

        var result = await handler.Handle(new GetOrder(id));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new OrderDto(id, "Ada Lovelace", 42.50m));
    }

    [Fact]
    public async Task Handle_should_return_not_found_for_unknown_id()
    {
        var handler = new GetOrderHandler(new FakeQueryDb());

        var result = await handler.Handle(new GetOrder(Guid.NewGuid()));

        result.IsSuccess.ShouldBeFalse();
        result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
    }

    private sealed class FakeQueryDb(params Order[] orders) : IQueryDb
    {
        public IQueryable<Order> Orders { get; } = orders.AsQueryable();
    }
}
