using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Manufacturing.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="ManufacturingDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="ManufacturingDbContext"/> via DI
/// (see <see cref="ManufacturingModule.AddManufacturingModule"/>).
/// </summary>
public class ManufacturingDbContextFactory : IDesignTimeDbContextFactory<ManufacturingDbContext>
{
    public ManufacturingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "manufacturing"))
            .Options;

        return new ManufacturingDbContext(options, new DesignTimeCompanyContext(), NullLogger<ManufacturingDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
