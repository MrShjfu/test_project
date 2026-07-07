using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.Planning.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="PlanningDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="PlanningDbContext"/> via DI
/// (see <see cref="PlanningModule.AddPlanningModule"/>).
/// </summary>
public class PlanningDbContextFactory : IDesignTimeDbContextFactory<PlanningDbContext>
{
    public PlanningDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlanningDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "planning"))
            .Options;

        return new PlanningDbContext(options, new DesignTimeCompanyContext(), NullLogger<PlanningDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
