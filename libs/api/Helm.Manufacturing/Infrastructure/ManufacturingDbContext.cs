using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Manufacturing.Infrastructure;

public class ManufacturingDbContext(
    DbContextOptions<ManufacturingDbContext> options,
    ICompanyContext company,
    ILogger<ManufacturingDbContext> logger)
    : ModuleDbContext<ManufacturingDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("manufacturing");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
