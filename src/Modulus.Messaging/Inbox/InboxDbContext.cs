using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Inbox;

public sealed class InboxDbContext : DbContext
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<InboxMessageConsumer> InboxMessageConsumers => Set<InboxMessageConsumer>();

    public InboxDbContext(DbContextOptions<InboxDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.OccurredOnUtc).IsRequired();
            entity.Property(e => e.ProcessedOnUtc);

            // Polling query: WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc.
            entity.HasIndex(e => new { e.ProcessedOnUtc, e.OccurredOnUtc });

            // Retention purge: WHERE OccurredOnUtc < @cutoff — the composite above cannot
            // seek on OccurredOnUtc alone, and retention targets exactly the large tables
            // where a full scan per batch hurts most.
            entity.HasIndex(e => e.OccurredOnUtc);
        });

        modelBuilder.Entity<InboxMessageConsumer>(entity =>
        {
            entity.HasKey(e => new { e.InboxMessageId, e.Name });
            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ReservedOnUtc).IsRequired();
            entity.Property(e => e.ProcessedOnUtc);
        });
    }
}
