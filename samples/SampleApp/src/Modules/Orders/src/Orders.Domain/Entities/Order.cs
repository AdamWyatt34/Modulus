using SampleApp.BuildingBlocks.Domain.Entities;

namespace SampleApp.Orders.Domain.Entities;

public class Order : AggregateRoot<Guid>
{
    public string CustomerName { get; private set; } = default!;
    public decimal Total { get; private set; } = default!;

    private Order() { }

    public static Order Create(Guid id, string customerName, decimal total)
    {
        return new Order { Id = id, CustomerName = customerName, Total = total };
    }
}
