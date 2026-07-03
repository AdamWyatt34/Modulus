using Microsoft.EntityFrameworkCore;
using SampleApp.Notifications.Application.Data;

namespace SampleApp.Notifications.Infrastructure.Persistence;

/// <summary>
/// Read-only DbContext for query operations. Uses NoTracking by default.
/// Add IQueryable properties matching those declared in <see cref="IQueryDb"/>.
/// </summary>
public sealed class NotificationsReadOnlyDbContext : DbContext, IQueryDb
{
    public NotificationsReadOnlyDbContext(DbContextOptions<NotificationsReadOnlyDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("notifications");

        // Apply the same entity configurations as the write DbContext
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsReadOnlyDbContext).Assembly);
    }
}
