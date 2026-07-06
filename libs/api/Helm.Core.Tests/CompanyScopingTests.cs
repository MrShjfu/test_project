using FluentAssertions;
using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

// Not `file`-scoped: EF Core 10's NavigationExpandingExpressionVisitor throws
// IndexOutOfRangeException when a file-local type is used as an entity CLR type together with
// a per-instance HasQueryFilter (confirmed via isolated repro — WidgetDb/Ctx below stay file-local
// without issue; only the entity type itself trips the bug).
public class Widget : CompanyOwnedEntity { public Guid Id { get; init; } = Guid.NewGuid(); }

// Tiny in-test spy so audit tests can assert on log content instead of just "didn't throw".
// File-local TYPE is fine here — it's not an entity CLR type, so it doesn't trip the EF bug above.
file sealed class SpyLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

file class WidgetDb(DbContextOptions<WidgetDb> o, ICompanyContext c, ILogger<WidgetDb> logger)
    : ModuleDbContext<WidgetDb>(o, c, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("widget_test");
        b.Entity<Widget>();
        base.OnModelCreating(b); // MUST be last — applies company filters
    }
}

file record Ctx(string CompanyId, bool IsGroupAdmin) : ICompanyContext;

[Collection("postgres")]
public class CompanyScopingTests(PostgresFixture pg)
{
    [Fact]
    public async Task Rows_are_scoped_by_company_and_admin_sees_all()
    {
        // EF's Database.EnsureCreatedAsync() no-ops once the physical database already exists
        // (it checks "does the database exist", not "does this model's schema exist") — but it
        // *does* create the database itself when it doesn't. The "postgres" collection shares one
        // Testcontainers server across test classes, and CoreDbContextTests.MigrateAsync() already
        // populates the default "postgres" database — so this test points at its own database name
        // on that same server, independent of run order, and lets EnsureCreatedAsync create it.
        var connectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
        { Database = "widget_scoping_test" }.ConnectionString;

        // Local function, not a member method: file-local types (WidgetDb, ICompanyContext's
        // file-local Ctx caller) cannot appear in a member signature of a non-file-local type
        // (CS9051) — a local function inside the test body is exempt from that rule.
        WidgetDb Db(ICompanyContext ctx, ILogger<WidgetDb>? logger = null) =>
            new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(connectionString).Options, ctx,
                logger ?? new SpyLogger<WidgetDb>());

        await using (var setup = Db(new Ctx("doyle", false)))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(new Widget()); // CompanyId auto-stamped "doyle"
            await setup.SaveChangesAsync();
        }
        await using (var north = Db(new Ctx("north", false)))
        {
            north.Add(new Widget());
            await north.SaveChangesAsync();
            (await north.Set<Widget>().CountAsync()).Should().Be(1); // only north's
        }
        await using var admin = Db(new Ctx("ntg", true));
        (await admin.Set<Widget>().CountAsync()).Should().Be(2);      // bypass
    }

    [Fact]
    public async Task Admin_context_logs_audit_once_per_instance_non_admin_logs_none()
    {
        // Own database: this test's SaveChanges are irrelevant to Rows_are_scoped's counts,
        // but sharing "widget_scoping_test" would still pollute other tests' widgets table.
        var connectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
        { Database = "widget_audit_test" }.ConnectionString;

        WidgetDb Db(ICompanyContext ctx, ILogger<WidgetDb> logger) =>
            new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(connectionString).Options, ctx, logger);

        var nonAdminSpy = new SpyLogger<WidgetDb>();
        await using (var nonAdmin = Db(new Ctx("doyle", false), nonAdminSpy))
        {
            await nonAdmin.Database.EnsureCreatedAsync();
            await nonAdmin.Set<Widget>().CountAsync();
        }
        nonAdminSpy.Entries.Should().BeEmpty();

        var adminSpy1 = new SpyLogger<WidgetDb>();
        await using (var admin1 = Db(new Ctx("ntg", true), adminSpy1))
        {
            await admin1.Set<Widget>().CountAsync();
        }
        adminSpy1.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("AUDIT"));

        var adminSpy2 = new SpyLogger<WidgetDb>();
        await using (var admin2 = Db(new Ctx("ntg", true), adminSpy2))
        {
            await admin2.Set<Widget>().CountAsync();
        }
        adminSpy2.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("AUDIT"));

        // Two separate admin instances -> two separate audit log entries (one each),
        // proving the audit fires per-instance rather than once per context TYPE.
        (adminSpy1.Entries.Count + adminSpy2.Entries.Count).Should().Be(2);
    }

    [Fact]
    public async Task NonAdmin_write_with_foreign_company_id_throws()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
        { Database = "widget_spoof_nonadmin_test" }.ConnectionString;

        WidgetDb Db(ICompanyContext ctx) =>
            new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(connectionString).Options, ctx,
                new SpyLogger<WidgetDb>());

        await using var db = Db(new Ctx("doyle", false));
        await db.Database.EnsureCreatedAsync();
        db.Add(new Widget { CompanyId = "north" }); // pre-set to a foreign company

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-company write denied*");
    }

    [Fact]
    public async Task GroupAdmin_write_with_foreign_company_id_succeeds()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
        { Database = "widget_spoof_admin_test" }.ConnectionString;

        WidgetDb Db(ICompanyContext ctx) =>
            new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(connectionString).Options, ctx,
                new SpyLogger<WidgetDb>());

        await using var db = Db(new Ctx("ntg", true));
        await db.Database.EnsureCreatedAsync();
        db.Add(new Widget { CompanyId = "north" }); // group admin may write on behalf of another company

        await db.Invoking(async d => await d.SaveChangesAsync()).Should().NotThrowAsync();
    }
}
