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
            e.Property(m => m.EventType).IsRequired();
            e.Property(m => m.Payload).IsRequired();
        });

        b.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events");
            e.HasKey(p => p.EventId);
        });
    }
}
