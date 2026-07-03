namespace SampleApp.Orders.Application.Queries.GetOrder;

public sealed record OrderDto(Guid Id, string CustomerName, decimal Total);
