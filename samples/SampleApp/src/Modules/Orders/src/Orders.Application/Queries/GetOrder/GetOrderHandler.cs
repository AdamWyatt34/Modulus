using Modulus.Mediator.Abstractions;
using SampleApp.Orders.Application.Data;

namespace SampleApp.Orders.Application.Queries.GetOrder;

public sealed class GetOrderHandler(IQueryDb queryDb) : IQueryHandler<GetOrder, OrderDto>
{
    public Task<Result<OrderDto>> Handle(GetOrder query, CancellationToken cancellationToken = default)
    {
        var order = queryDb.Orders
            .Where(o => o.Id == query.OrderId)
            .Select(o => new OrderDto(o.Id, o.CustomerName, o.Total))
            .FirstOrDefault();

        Result<OrderDto> result = order is not null
            ? order
            : Error.NotFound("Order.NotFound", $"Order '{query.OrderId}' was not found.");

        return Task.FromResult(result);
    }
}
