using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Application.Data;

/// <summary>
/// Read-only data access interface for queries (CQRS read side).
/// Implemented by OrdersReadOnlyDbContext in the Infrastructure layer.
/// </summary>
public interface IQueryDb
{
    IQueryable<Order> Orders { get; }
}
