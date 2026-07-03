using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Persistence;

namespace SampleApp.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    IMediator mediator) : BaseDbContext(options, mediator)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("orders");

        // Apply entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}
