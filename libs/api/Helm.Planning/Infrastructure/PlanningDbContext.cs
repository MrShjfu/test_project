using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Planning.Infrastructure;

public class PlanningDbContext(
    DbContextOptions<PlanningDbContext> options,
    ICompanyContext company,
    ILogger<PlanningDbContext> logger)
    : ModuleDbContext<PlanningDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("planning");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
