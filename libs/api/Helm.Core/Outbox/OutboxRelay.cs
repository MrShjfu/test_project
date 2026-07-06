using Helm.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Helm.Core.Outbox;

/// <summary>
/// Polls <c>{schema}.outbox</c> every second and republishes unprocessed rows via <see cref="IEventBus"/>.
/// Safe to run one instance per replica: <c>FOR UPDATE SKIP LOCKED</c> lets concurrent pollers
/// (this module, N replicas) each grab a disjoint batch instead of blocking or double-publishing.
/// Registered once per publishing module by that module's <c>Add&lt;Mod&gt;Module</c> extension.
/// </summary>
public class OutboxRelay<TContext>(
    IServiceScopeFactory scopeFactory,
    IEventBus bus,
    ILogger<OutboxRelay<TContext>> logger,
    IConfiguration? config = null) : BackgroundService
    where TContext : DbContext
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = config?.GetValue("Outbox:RelayEnabled", true) ?? true;
        if (!enabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox relay iteration failed");
            }
        }
    }

    /// <summary>One poll-publish-mark batch. Exposed internal for tests (see InternalsVisibleTo).</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var schema = db.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no default schema configured.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // schema is derived from the context's own model (not user input); BatchSize is a compile-time
        // constant. FromSqlRaw/ExecuteSqlRaw are acceptable for a module's own schema per rule 1 — the
        // interpolated {schema} segment names a trusted identifier, not a value, so SqlQuery's
        // parameterization (which targets values, not identifiers) wouldn't apply here anyway.
        // Column names are unquoted snake_case (id, event_type, payload, ...) per ADR-004 DDL, as
        // mapped explicitly by OutboxModelBuilder — aliased below so SqlQueryRaw's result-to-property
        // matching (which goes by CLR property name) lines up with OutboxRow's PascalCase members.
#pragma warning disable EF1002
        var rows = await db.Database.SqlQueryRaw<OutboxRow>(
                $"""
                 SELECT id AS "Id", event_type AS "EventType", payload AS "Payload" FROM {schema}.outbox
                 WHERE processed_at IS NULL ORDER BY created_at
                 FOR UPDATE SKIP LOCKED LIMIT {BatchSize}
                 """)
            .ToListAsync(ct);
#pragma warning restore EF1002

        foreach (var row in rows)
        {
            var envelope = new EventEnvelope(row.Id, row.EventType, row.Payload);
            await bus.PublishAsync(envelope, ct);

#pragma warning disable EF1002 // {schema} is a trusted identifier (this context's own model), not a value
            await db.Database.ExecuteSqlRawAsync(
                $"""UPDATE {schema}.outbox SET processed_at = now() WHERE id = @id""",
                [new Npgsql.NpgsqlParameter("id", row.Id)], ct);
#pragma warning restore EF1002
        }

        await tx.CommitAsync(ct);
    }

    // SqlQueryRaw<T> for an unmapped type matches result columns to properties by CLR property
    // name (not [Column] metadata) — the SELECT above aliases each column to match exactly,
    // quoted so Postgres preserves the PascalCase instead of folding to lowercase.
    private sealed class OutboxRow
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = "";
        public string Payload { get; init; } = "";
    }
}
