using Microsoft.EntityFrameworkCore;

namespace Helm.Core.Outbox;

/// <summary>
/// Maps the outbox/idempotency tables into the calling module DbContext's own default schema
/// (never a shared schema — ADR-001/002). Every module DbContext must call this from
/// <c>OnModelCreating</c> after <c>HasDefaultSchema</c> is set.
/// </summary>
public static class OutboxModelBuilder
{
    public static void AddOutbox(ModelBuilder b)
    {
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.EventType).HasColumnName("event_type").IsRequired();
            e.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });

        b.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events");
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).HasColumnName("event_id");
            e.Property(p => p.ProcessedAt).HasColumnName("processed_at");
        });
    }
}
