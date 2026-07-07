using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Fulfilment.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="FulfilmentDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="FulfilmentDbContext"/> via DI
/// (see <see cref="FulfilmentModule.AddFulfilmentModule"/>).
/// </summary>
public class FulfilmentDbContextFactory : IDesignTimeDbContextFactory<FulfilmentDbContext>
{
    public FulfilmentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FulfilmentDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "fulfilment"))
            .Options;

        return new FulfilmentDbContext(options, new DesignTimeCompanyContext(), NullLogger<FulfilmentDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
