using Microsoft.EntityFrameworkCore;
using SampleApp.Orders.Application.Data;

namespace SampleApp.Orders.Infrastructure.Persistence;

/// <summary>
/// Read-only DbContext for query operations. Uses NoTracking by default.
/// Add IQueryable properties matching those declared in <see cref="IQueryDb"/>.
/// </summary>
public sealed class OrdersReadOnlyDbContext : DbContext, IQueryDb
{
    public OrdersReadOnlyDbContext(DbContextOptions<OrdersReadOnlyDbContext> options)
        : base(options)
    {
    }

    public IQueryable<SampleApp.Orders.Domain.Entities.Order> Orders
        => Set<SampleApp.Orders.Domain.Entities.Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("orders");

        // Apply the same entity configurations as the write DbContext
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersReadOnlyDbContext).Assembly);
    }
}
