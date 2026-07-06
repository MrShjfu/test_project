using System.Text.Json;
using FluentAssertions;
using Helm.Core.Messaging;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Helm.Core.Tests;

/// <summary>Minimal module DbContext used only to exercise the outbox/idempotency contract in isolation.</summary>
public class OutboxTestDb(DbContextOptions<OutboxTestDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("outbox_test");
        OutboxModelBuilder.AddOutbox(b);
    }
}

public record TestEvent(Guid EventId, string Message) : IDomainEvent;

/// <summary>Records every TestEvent it handles so tests can assert HandleAsync ran exactly once.</summary>
public class RecordingConsumer(IServiceScopeFactory scopeFactory, IEventBus bus)
    : IdempotentConsumer<OutboxTestDb, TestEvent>(scopeFactory, bus)
{
    public List<TestEvent> Handled { get; } = [];

    protected override Task HandleAsync(TestEvent evt, OutboxTestDb db)
    {
        Handled.Add(evt);
        return Task.CompletedTask;
    }
}

[Collection("postgres")]
public class OutboxTests(PostgresFixture pg)
{
    // Tests share one Postgres container (via the "postgres" collection fixture) for speed, so
    // each [Fact] must explicitly clear the outbox/processed_events tables exactly once up front
    // — CleanDbAsync truncates, OpenDbAsync just opens a fresh context against the same data.
    private async Task<OutboxTestDb> CleanDbAsync()
    {
        var db = await OpenDbAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_test.outbox, outbox_test.processed_events");
        return db;
    }

    private Task<OutboxTestDb> OpenDbAsync() =>
        Task.FromResult(pg.CreateDbContext<OutboxTestDb>(o => new OutboxTestDb(o)));

    [Fact]
    public async Task Outbox_table_uses_snake_case_columns_per_adr004()
    {
        await using var db = await CleanDbAsync();

        var columns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT column_name FROM information_schema.columns
                WHERE table_schema = 'outbox_test' AND table_name = 'outbox'
                """)
            .ToListAsync();

        columns.Should().BeEquivalentTo(["id", "event_type", "payload", "created_at", "processed_at"]);
    }

    [Fact]
    public async Task Rollback_leaves_no_outbox_row()
    {
        await using var db = await CleanDbAsync();

        try
        {
            OutboxWriter.Enqueue(db, new TestEvent(Guid.NewGuid(), "won't persist"));
            throw new InvalidOperationException("simulated failure before SaveChanges");
        }
        catch (InvalidOperationException)
        {
            // swallow — the point is we never called SaveChangesAsync
        }

        await using var freshDb = await OpenDbAsync();
        var count = await freshDb.Set<OutboxMessage>().CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Relay_publishes_and_marks_processed()
    {
        var evt = new TestEvent(Guid.NewGuid(), "hello");
        await using var seedDb = await CleanDbAsync();
        OutboxWriter.Enqueue(seedDb, evt);
        await seedDb.SaveChangesAsync();

        var bus = new InMemoryEventBus();
        var received = new List<EventEnvelope>();
        using var _ = bus.Subscribe(nameof(TestEvent), (e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(bus);
        services.AddDbContext<OutboxTestDb>(o => o.UseNpgsql(pg.ConnectionString));
        var provider = services.BuildServiceProvider();

        var relay = new OutboxRelay<OutboxTestDb>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            bus,
            NullLogger<OutboxRelay<OutboxTestDb>>.Instance);

        await relay.RunOnceAsync(CancellationToken.None);

        received.Should().HaveCount(1);
        received[0].EventType.Should().Be(nameof(TestEvent));

        await using var checkDb = await OpenDbAsync();
        var row = await checkDb.Set<OutboxMessage>().SingleAsync();
        row.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Relay_competing_instances_do_not_double_publish()
    {
        const int rowCount = 5;
        await using var seedDb = await CleanDbAsync();
        for (var i = 0; i < rowCount; i++)
            OutboxWriter.Enqueue(seedDb, new TestEvent(Guid.NewGuid(), $"row-{i}"));
        await seedDb.SaveChangesAsync();

        var bus = new InMemoryEventBus();
        var receivedCount = 0;
        using var _ = bus.Subscribe(nameof(TestEvent), (_, _) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(bus);
        services.AddDbContext<OutboxTestDb>(o => o.UseNpgsql(pg.ConnectionString));
        var provider = services.BuildServiceProvider();

        var relayA = new OutboxRelay<OutboxTestDb>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            bus,
            NullLogger<OutboxRelay<OutboxTestDb>>.Instance);
        var relayB = new OutboxRelay<OutboxTestDb>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            bus,
            NullLogger<OutboxRelay<OutboxTestDb>>.Instance);

        // Two competing relay iterations racing over the same 5 rows: SKIP LOCKED must
        // partition the rows between them (or serialize) so nothing is delivered twice.
        await Task.WhenAll(
            relayA.RunOnceAsync(CancellationToken.None),
            relayB.RunOnceAsync(CancellationToken.None));

        receivedCount.Should().Be(rowCount);

        await using var checkDb = await OpenDbAsync();
        var unprocessed = await checkDb.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null);
        unprocessed.Should().Be(0);
    }

    [Fact]
    public async Task IdempotentConsumer_skips_duplicate_event_id()
    {
        await using var db = await CleanDbAsync();

        var bus = new InMemoryEventBus();
        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(bus);
        services.AddDbContext<OutboxTestDb>(o => o.UseNpgsql(pg.ConnectionString));
        var provider = services.BuildServiceProvider();

        var consumer = new RecordingConsumer(provider.GetRequiredService<IServiceScopeFactory>(), bus);
        await consumer.StartAsync(CancellationToken.None);

        var evt = new TestEvent(Guid.NewGuid(), "duplicate-me");
        var envelope = new EventEnvelope(evt.EventId, nameof(TestEvent), JsonSerializer.Serialize(evt));

        await bus.PublishAsync(envelope);
        await bus.PublishAsync(envelope); // redelivered — at-least-once semantics

        consumer.Handled.Should().HaveCount(1);
        consumer.Handled[0].EventId.Should().Be(evt.EventId);

        await consumer.StopAsync(CancellationToken.None);
    }
}
