using Helm.Core.Messaging;
using Helm.Core.MultiCompany;
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
///
/// <remarks>
/// Disclosed change (Task 15): resolving <typeparamref name="TContext"/> via
/// <c>scope.ServiceProvider.GetRequiredService&lt;TContext&gt;()</c> — the original, only, way —
/// throws "no http context" for every real module today, because <c>ICompanyContext</c> (required
/// by every <c>ModuleDbContext</c> constructor) is registered only as scoped-from-HTTP-request
/// (see <c>HelmAuthExtensions.AddHelmAuth</c>). This was latent since Task 9/11 (every module
/// registers <c>OutboxRelay&lt;ItsDbContext&gt;</c> the same way) but never observed because every
/// prior module test disables the relay (<c>Outbox:RelayEnabled=false</c>) — Task 15 is the first
/// to turn it on for a real end-to-end test and hit it directly. The outbox/processed_events
/// tables the relay touches are not company-owned (no CompanyId, no query filter — see
/// OutboxModelBuilder), so which company identity satisfies the DbContext constructor is
/// immaterial to correctness; <see cref="ResolveDbContext"/> supplies a fixed system identity via
/// <see cref="ActivatorUtilities"/> only when <typeparamref name="TContext"/>'s constructor actually
/// declares an <see cref="ICompanyContext"/> parameter (detected by reflection, not by probing with
/// try/catch) — so <c>OutboxTestDb</c> (Helm.Core.Tests' plain DbContext with no such parameter)
/// keeps resolving via plain DI, unaffected.
/// </remarks>
public class OutboxRelay<TContext>(
    IServiceScopeFactory scopeFactory,
    IEventBus bus,
    ILogger<OutboxRelay<TContext>> logger,
    IConfiguration? config = null) : BackgroundService
    where TContext : DbContext
{
    private const int BatchSize = 20;

    // GetConstructors() (no BindingFlags) returns public instance constructors only — true for
    // every current ModuleDbContext subclass, which all use a public primary constructor. If a
    // future ModuleDbContext ever hides its ICompanyContext-accepting constructor as non-public,
    // this would stop matching and ResolveDbContext would silently fall back to the pre-Task-15
    // GetRequiredService path (reproducing the "no http context" failure this hook exists to
    // avoid) — so keep module DbContext constructors public.
    private static readonly bool RequiresCompanyContext =
        typeof(TContext).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(ICompanyContext));

    /// <summary>
    /// Resolves <typeparamref name="TContext"/> for this poll iteration. See the type-level remarks
    /// for why a <c>ModuleDbContext</c>-shaped TContext can't simply be resolved via
    /// <c>GetRequiredService</c> in a background service, and why a fixed system identity (rather
    /// than a per-event one, unlike IdempotentConsumer) is correct here: the outbox/processed_events
    /// tables are module-schema-wide bookkeeping, not company-owned data.
    /// </summary>
    protected virtual TContext ResolveDbContext(IServiceScope scope) =>
        RequiresCompanyContext
            ? ActivatorUtilities.CreateInstance<TContext>(scope.ServiceProvider, new FixedCompanyContext("ntg-system"))
            : scope.ServiceProvider.GetRequiredService<TContext>();

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
        var db = ResolveDbContext(scope);
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
