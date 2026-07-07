using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.AfterSales.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="AfterSalesDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="AfterSalesDbContext"/> via DI
/// (see <see cref="AfterSalesModule.AddAfterSalesModule"/>).
/// </summary>
public class AfterSalesDbContextFactory : IDesignTimeDbContextFactory<AfterSalesDbContext>
{
    public AfterSalesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AfterSalesDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "after_sales"))
            .Options;

        return new AfterSalesDbContext(options, new DesignTimeCompanyContext(), NullLogger<AfterSalesDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
