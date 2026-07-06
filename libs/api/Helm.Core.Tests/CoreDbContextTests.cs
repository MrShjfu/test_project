using FluentAssertions;
using Helm.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Collection("postgres")]
public class CoreDbContextTests(PostgresFixture pg)
{
    [Fact]
    public async Task Migrates_and_stores_company_and_user()
    {
        await using var db = pg.CreateDbContext<CoreDbContext>(
            o => new CoreDbContext(o),
            o => o.MigrationsHistoryTable("__ef_migrations", "core"));
        await db.Database.MigrateAsync();
        db.Add(new Company { Id = "doyle", Name = "Doyle" });
        db.Add(new User { Id = Guid.NewGuid(), Email = "a@doyle.com", CompanyId = "doyle", ModuleRoles = ["crm:editor"] });
        await db.SaveChangesAsync();

        var historyTableCount = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema='core' AND table_name='__ef_migrations'")
            .SingleAsync();
        historyTableCount.Should().Be(1);

        await using var freshDb = pg.CreateDbContext<CoreDbContext>(
            o => new CoreDbContext(o),
            o => o.MigrationsHistoryTable("__ef_migrations", "core"));
        (await freshDb.Set<User>().AsNoTracking().SingleAsync()).CompanyId.Should().Be("doyle");
    }
}
