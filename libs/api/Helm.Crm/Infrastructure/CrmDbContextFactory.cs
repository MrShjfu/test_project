using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Crm.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="CrmDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="CrmDbContext"/> via DI
/// (see <see cref="CrmModule.AddCrmModule"/>).
/// </summary>
public class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "crm"))
            .Options;

        return new CrmDbContext(options, new DesignTimeCompanyContext(), NullLogger<CrmDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
