using SampleApp.BuildingBlocks.Application.Persistence;
using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Domain.Repositories;

public interface IOrderRepository : IRepository<Order, Guid>
{
}
