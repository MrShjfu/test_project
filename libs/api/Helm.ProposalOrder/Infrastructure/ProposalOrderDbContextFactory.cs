using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helm.ProposalOrder.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add/database update` can construct a
/// <see cref="ProposalOrderDbContext"/> outside an HTTP request, where the real DI-resolved
/// <see cref="ICompanyContext"/> (bound to the current request's claims) is unavailable.
/// Never used at runtime — the app always resolves <see cref="ProposalOrderDbContext"/> via DI
/// (see <see cref="ProposalOrderModule.AddProposalOrderModule"/>).
/// </summary>
public class ProposalOrderDbContextFactory : IDesignTimeDbContextFactory<ProposalOrderDbContext>
{
    public ProposalOrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProposalOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=helm_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "proposal_order"))
            .Options;

        return new ProposalOrderDbContext(options, new DesignTimeCompanyContext(), NullLogger<ProposalOrderDbContext>.Instance);
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public string CompanyId => "design-time";
        public bool IsGroupAdmin => false;
    }
}
