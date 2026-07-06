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
        await using var db = pg.CreateDbContext<CoreDbContext>(o => new CoreDbContext(o));
        await db.Database.MigrateAsync();
        db.Add(new Company { Id = "doyle", Name = "Doyle" });
        db.Add(new User { Id = Guid.NewGuid(), Email = "a@doyle.com", CompanyId = "doyle", ModuleRoles = ["crm:editor"] });
        await db.SaveChangesAsync();
        (await db.Set<User>().SingleAsync()).CompanyId.Should().Be("doyle");
    }
}
