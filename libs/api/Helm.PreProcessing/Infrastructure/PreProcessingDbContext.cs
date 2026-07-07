using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.PreProcessing.Infrastructure;

public class PreProcessingDbContext(
    DbContextOptions<PreProcessingDbContext> options,
    ICompanyContext company,
    ILogger<PreProcessingDbContext> logger)
    : ModuleDbContext<PreProcessingDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("pre_processing");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
