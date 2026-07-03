using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using SampleApp.BuildingBlocks.Infrastructure.Persistence;

namespace SampleApp.Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    IMediator mediator) : BaseDbContext(options, mediator)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("notifications");

        // Apply entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
    }
}
