using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.PreProcessing.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="PreProcessingDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="PreProcessingDbContext"/> via DI
/// (see <see cref="PreProcessingModule.AddPreProcessingModule"/>).
/// </summary>
public class PreProcessingDbContextFactory : IDesignTimeDbContextFactory<PreProcessingDbContext>
{
    public PreProcessingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PreProcessingDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "pre_processing"))
            .Options;

        return new PreProcessingDbContext(options, new DesignTimeCompanyContext(), NullLogger<PreProcessingDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
