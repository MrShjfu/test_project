using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Design.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="DesignDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="DesignDbContext"/> via DI
/// (see <see cref="DesignModule.AddDesignModule"/>).
/// </summary>
public class DesignDbContextFactory : IDesignTimeDbContextFactory<DesignDbContext>
{
    public DesignDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DesignDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "design"))
            .Options;

        return new DesignDbContext(options, new DesignTimeCompanyContext(), NullLogger<DesignDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
