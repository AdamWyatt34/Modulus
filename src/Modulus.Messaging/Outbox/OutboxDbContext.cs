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
            // W3C traceparent is exactly 55 chars; tracestate is spec-capped for propagation.
            entity.Property(e => e.TraceParent).HasMaxLength(55);
            entity.Property(e => e.TraceState).HasMaxLength(512);

            // Polling query: WHERE ProcessedAt IS NULL AND Attempts < N
            //   AND (NextAttemptOnUtc IS NULL OR NextAttemptOnUtc <= @now) ORDER BY CreatedAt.
            entity.HasIndex(e => new { e.ProcessedAt, e.NextAttemptOnUtc, e.CreatedAt });
        });
    }
}
