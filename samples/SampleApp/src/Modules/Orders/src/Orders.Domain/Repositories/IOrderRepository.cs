using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Domain.Repositories;

// Self-contained on purpose: Domain must not depend on BuildingBlocks.Application.
// OrderRepository (Infrastructure) inherits EfRepository<Order, Guid>, whose members
// satisfy this interface by signature.
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Order entity, CancellationToken cancellationToken = default);

    void Update(Order entity);

    void Remove(Order entity);
}
