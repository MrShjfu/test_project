using FluentAssertions;
using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Not `file`-scoped: EF Core 10's NavigationExpandingExpressionVisitor throws
// IndexOutOfRangeException when a file-local type is used as an entity CLR type together with
// a per-instance HasQueryFilter (confirmed via isolated repro — WidgetDb/Ctx below stay file-local
// without issue; only the entity type itself trips the bug).
public class Widget : CompanyOwnedEntity { public Guid Id { get; init; } = Guid.NewGuid(); }

file class WidgetDb(DbContextOptions<WidgetDb> o, ICompanyContext c)
    : ModuleDbContext<WidgetDb>(o, c, NullLogger<WidgetDb>.Instance)
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
        WidgetDb Db(ICompanyContext ctx) =>
            new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(connectionString).Options, ctx);

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
}
