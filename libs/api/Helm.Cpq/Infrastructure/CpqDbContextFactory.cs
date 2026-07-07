using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Cpq.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="CpqDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="CpqDbContext"/> via DI
/// (see <see cref="CpqModule.AddCpqModule"/>).
/// </summary>
public class CpqDbContextFactory : IDesignTimeDbContextFactory<CpqDbContext>
{
    public CpqDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CpqDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "cpq"))
            .Options;

        return new CpqDbContext(options, new DesignTimeCompanyContext(), NullLogger<CpqDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
