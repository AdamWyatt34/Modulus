using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

public sealed class OutboxDbContext : DbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ProcessedAt);
            entity.Property(e => e.Attempts).IsRequired();
            entity.Property(e => e.LastError);
            entity.Property(e => e.NextAttemptOnUtc);
            entity.Property(e => e.ScheduledOnUtc);
            // W3C traceparent is exactly 55 chars; tracestate is spec-capped for propagation.
            entity.Property(e => e.TraceParent).HasMaxLength(55);
            entity.Property(e => e.TraceState).HasMaxLength(512);
            // Generous but bounded: "{MachineName}:{32-char GUID N-format}" comfortably fits;
            // custom owner-id schemes (e.g. a pod name) still have ample room.
            entity.Property(e => e.ClaimedBy).HasMaxLength(100);
            entity.Property(e => e.ClaimedUntil);

            // Claim query: WHERE ProcessedAt IS NULL AND Attempts < N
            //   AND (NextAttemptOnUtc IS NULL OR NextAttemptOnUtc <= @now)
            //   AND (ScheduledOnUtc IS NULL OR ScheduledOnUtc <= @now)
            //   AND (ClaimedUntil IS NULL OR ClaimedUntil < @now) ORDER BY CreatedAt.
            entity.HasIndex(e => new { e.ProcessedAt, e.NextAttemptOnUtc, e.ScheduledOnUtc, e.ClaimedUntil, e.CreatedAt });
        });
    }
}
