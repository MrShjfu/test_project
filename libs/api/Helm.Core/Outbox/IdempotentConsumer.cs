using System.Text.Json;
using Helm.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Core.Outbox;

/// <summary>
/// Base class for at-least-once event consumers. Subscribes at startup; on receipt, records the
/// event id in <c>{schema}.processed_events</c> before invoking <see cref="HandleAsync"/> so a
/// redelivered envelope (broker at-least-once, relay retry) is a no-op rather than a repeat side
/// effect. The insert-first-then-handle ordering means a duplicate is caught by the table's PK
/// constraint (Postgres SQLSTATE 23505) rather than a racy read-then-write check.
/// </summary>
public abstract class IdempotentConsumer<TContext, TEvent>(
    IServiceScopeFactory scopeFactory,
    IEventBus bus) : IHostedService
    where TContext : DbContext
    where TEvent : IDomainEvent
{
    private const string UniqueViolation = "23505";
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = bus.Subscribe(typeof(TEvent).Name, OnEnvelopeAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    protected abstract Task HandleAsync(TEvent evt, TContext db);

    /// <summary>
    /// Disclosed change (Task 15): supplies <typeparamref name="TContext"/> for this envelope.
    /// Default resolves it from the scope's DI container — the original, still-current behavior
    /// for any consumer that doesn't override this. A consumer whose TContext requires a
    /// per-request <c>ICompanyContext</c> (e.g. CpqDbContext, ADR-003) has no ambient HTTP request
    /// to resolve one from here, so it overrides this hook to construct TContext itself with an
    /// <c>ICompanyContext</c> derived from the event's own company id — never a global/fixed
    /// company (see FixedCompanyContext). Takes the deserialized event so that derivation is
    /// possible; this is why deserialization was moved earlier in <see cref="OnEnvelopeAsync"/>.
    /// </summary>
    protected virtual TContext ResolveDbContext(IServiceScope scope, TEvent evt) =>
        scope.ServiceProvider.GetRequiredService<TContext>();

    private async Task OnEnvelopeAsync(EventEnvelope envelope, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        // Deserialize before resolving TContext (reordered from the original resolve-then-deserialize
        // sequence) so ResolveDbContext can see the event and derive a per-event ICompanyContext from
        // it when overridden. Deserialization is pure/side-effect-free, so moving it earlier doesn't
        // change behavior for the default hook (which ignores evt) or for existing consumers/tests.
        var evt = JsonSerializer.Deserialize<TEvent>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"Could not deserialize payload for {envelope.EventType}.");

        var db = ResolveDbContext(scope, evt);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Add(new ProcessedEvent { EventId = envelope.EventId, ProcessedAt = DateTimeOffset.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Already processed — ack without re-running side effects (at-least-once delivery).
            await tx.RollbackAsync(ct);
            return;
        }

        await HandleAsync(evt, db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
