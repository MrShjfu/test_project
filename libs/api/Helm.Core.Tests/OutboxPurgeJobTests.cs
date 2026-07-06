using FluentAssertions;
using Helm.Core.Jobs;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Helm.Core.Tests;

/// <summary>Minimal module DbContext used only to create the outbox/idempotency tables for the purge job test.</summary>
public class PurgeTestDb(DbContextOptions<PurgeTestDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("purge_test");
        OutboxModelBuilder.AddOutbox(b);
    }
}

[Collection("postgres")]
public class OutboxPurgeJobTests(PostgresFixture pg)
{
    private const string Schema = "purge_test";

    // Deliberately not EnsureCreatedAsync(): it checks database-level (not per-schema) existence,
    // so once any other test in this shared-container collection has created any table,
    // EnsureCreatedAsync becomes a no-op here and never creates purge_test's own tables. DDL is
    // issued directly instead, matching the column definitions in OutboxModelBuilder.
    private async Task<PurgeTestDb> CleanDbAsync()
    {
        var db = pg.CreateDbContext<PurgeTestDb>(o => new PurgeTestDb(o));
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {Schema}");
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS {Schema}.outbox (
                id           UUID PRIMARY KEY,
                event_type   TEXT NOT NULL,
                payload      JSONB NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL,
                processed_at TIMESTAMPTZ NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS {Schema}.processed_events (
                event_id     UUID PRIMARY KEY,
                processed_at TIMESTAMPTZ NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {Schema}.outbox, {Schema}.processed_events");
        return db;
    }

    private IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helm"] = pg.ConnectionString
            })
            .Build();

    [Fact]
    public async Task Run_deletes_processed_rows_older_than_30_days_but_keeps_fresh_and_unprocessed_rows()
    {
        await using var db = await CleanDbAsync();

        var oldId = Guid.NewGuid();
        var freshProcessedId = Guid.NewGuid();
        var unprocessedId = Guid.NewGuid();

        var emptyPayload = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO purge_test.outbox (id, event_type, payload, created_at, processed_at)
            VALUES
                ({oldId}, 'OldEvent', {emptyPayload}::jsonb, now() - interval '40 days', now() - interval '31 days'),
                ({freshProcessedId}, 'FreshEvent', {emptyPayload}::jsonb, now() - interval '2 days', now() - interval '1 days'),
                ({unprocessedId}, 'UnprocessedEvent', {emptyPayload}::jsonb, now(), NULL)
            """);

        var oldProcessedEventId = Guid.NewGuid();
        var freshProcessedEventId = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO purge_test.processed_events (event_id, processed_at)
            VALUES
                ({oldProcessedEventId}, now() - interval '31 days'),
                ({freshProcessedEventId}, now() - interval '1 days')
            """);

        var job = new OutboxPurgeJob(BuildConfig());

        await job.Run([Schema]);

        var remainingOutboxIds = await db.Set<OutboxMessage>().Select(m => m.Id).ToListAsync();
        remainingOutboxIds.Should().BeEquivalentTo([freshProcessedId, unprocessedId]);

        var remainingProcessedEventIds = await db.Set<ProcessedEvent>().Select(p => p.EventId).ToListAsync();
        remainingProcessedEventIds.Should().BeEquivalentTo([freshProcessedEventId]);
    }
}
