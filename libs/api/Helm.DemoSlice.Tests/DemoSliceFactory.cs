using Helm.Core.MultiCompany;
using Helm.Cpq.Infrastructure;
using Helm.Crm.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;

namespace Helm.DemoSlice.Tests;

/// <summary>
/// Extends <see cref="HelmApiFactory"/> (via its Task-15-added `protected virtual` hooks — see
/// disclosed change on that class) with a real RabbitMQ Testcontainer, Messaging:Provider=RabbitMQ,
/// Outbox:RelayEnabled=true, and migration of Helm.Cpq's schema in addition to Core+Crm. Used by
/// OutboxEndToEndTests, which needs the full outbox-relay-broker-consumer pipeline running for
/// real rather than the InMemory bus the base factory uses for other module tests.
/// LogSink (for the ADR-003 AUDIT assertion) is inherited from HelmApiFactory — it is registered
/// unconditionally there, not something this subclass needs to add.
/// </summary>
public class DemoSliceFactory : HelmApiFactory
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management").Build();

    public string RabbitMqUri => _rabbitMq.GetConnectionString();

    protected override async Task MigrateExtraAsync()
    {
        // Base HelmApiFactory.InitializeAsync calls this after Core+Crm migrate but before any
        // test creates an HTTP client, so starting the RabbitMQ container here (rather than a
        // separate method the test would have to remember to call) guarantees it's up before
        // ConfigureWebHost first runs (which needs its connection string).
        await _rabbitMq.StartAsync();

        using var cpq = new CpqDbContext(
            new DbContextOptionsBuilder<CpqDbContext>()
                .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "cpq"))
                .Options,
            new FixedCompanyContext("ntg-system"),
            NullLogger<CpqDbContext>.Instance);
        await cpq.Database.MigrateAsync();
    }

    protected override void ConfigureExtraAppConfiguration(IConfigurationBuilder config)
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "RabbitMQ",
            ["Messaging:RabbitMq:Uri"] = _rabbitMq.GetConnectionString(),
            ["Outbox:RelayEnabled"] = "true",
        });
    }

    /// <summary>Runs a caller-supplied scalar SQL query against the cpq schema (parallel to
    /// HelmApiFactory.CountAsync, which is hardcoded to a system CrmDbContext / crm schema).</summary>
    public async Task<long> CountCpqAsync(string sql)
    {
        using var db = new CpqDbContext(
            new DbContextOptionsBuilder<CpqDbContext>()
                .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "cpq"))
                .Options,
            new FixedCompanyContext("ntg-system"),
            NullLogger<CpqDbContext>.Instance);
#pragma warning disable EF1002 // sql is the test's own literal string, not user input
        return await db.Database.SqlQueryRaw<long>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
#pragma warning restore EF1002
    }

    protected override async Task DisposeExtraAsync() => await _rabbitMq.DisposeAsync();
}
