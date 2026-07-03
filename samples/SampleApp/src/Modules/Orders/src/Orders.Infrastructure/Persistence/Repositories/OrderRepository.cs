using Microsoft.EntityFrameworkCore;
using SampleApp.BuildingBlocks.Infrastructure.Persistence;
using SampleApp.Orders.Domain.Entities;
using SampleApp.Orders.Domain.Repositories;

namespace SampleApp.Orders.Infrastructure.Persistence.Repositories;

public class OrderRepository(
    OrdersDbContext context) : EfRepository<Order, Guid>(context), IOrderRepository
{
}
