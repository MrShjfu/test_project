using FluentAssertions;
using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Helm.Crm.Contracts.Events;
using Helm.Crm.Domain;
using Helm.Crm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Helm.DemoSlice.Tests;

/// <summary>
/// Proves ADR-004 write/event atomicity directly against a DbContext, no HTTP layer involved:
/// staging a state change (Customer) and an outbox row (via OutboxWriter.Enqueue) in the same
/// change tracker, then abandoning the context WITHOUT calling SaveChangesAsync, must leave BOTH
/// tables untouched. One SaveChanges = one transaction = no phantom event without a corresponding
/// state change, and no orphaned state change without its announcing event.
/// </summary>
public sealed class NoPhantomEventTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var db = OpenDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private CrmDbContext OpenDb() => new(
        new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "crm"))
            .Options,
        new FixedCompanyContext("doyle"),
        NullLogger<CrmDbContext>.Instance);

    [Fact]
    public async Task Uncommitted_customer_and_event_leave_no_trace()
    {
        var customerId = Guid.NewGuid();

        using (var db = OpenDb())
        {
            var customer = new Customer { Id = customerId, Name = "Doyle Test Co", Email = "test@doyle.example" };
            db.Add(customer);
            OutboxWriter.Enqueue(db, new CustomerCreated(Guid.NewGuid(), customer.Id, "doyle", customer.Name));

            // Deliberately no SaveChangesAsync — the whole point of this test.
        }

        using var freshDb = OpenDb();
        var customerCount = await freshDb.Set<Customer>().CountAsync(c => c.Id == customerId);
        var outboxCount = await freshDb.Set<OutboxMessage>().CountAsync();

        customerCount.Should().Be(0, "the Customer insert was never committed");
        outboxCount.Should().Be(0, "the outbox row was never committed — no phantom event without its state change (ADR-004)");
    }
}
