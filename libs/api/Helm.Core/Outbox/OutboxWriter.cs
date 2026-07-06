using System.Text.Json;
using Helm.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Helm.Core.Outbox;

/// <summary>
/// Writes a domain event into the calling DbContext's outbox table. Must be called inside the
/// same unit of work as the state change it announces — the outbox row only becomes durable on
/// the caller's own <c>SaveChangesAsync</c>, giving exactly the "state change + event" atomicity
/// ADR-004 requires without a distributed transaction.
/// </summary>
public static class OutboxWriter
{
    public static void Enqueue(DbContext db, IDomainEvent evt)
    {
        var message = new OutboxMessage
        {
            // outbox row id IS the domain event id — one row per event; stable across relay retries
            // (ADR-004: processed_events.event_id keys on the event)
            Id = evt.EventId,
            EventType = evt.GetType().Name,
            Payload = JsonSerializer.Serialize(evt, evt.GetType()),
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = null,
        };
        db.Add(message);
    }
}
