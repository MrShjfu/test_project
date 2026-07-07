using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.AfterSales.Infrastructure;

public class AfterSalesDbContext(
    DbContextOptions<AfterSalesDbContext> options,
    ICompanyContext company,
    ILogger<AfterSalesDbContext> logger)
    : ModuleDbContext<AfterSalesDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("after_sales");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
