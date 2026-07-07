using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Fulfilment.Infrastructure;

public class FulfilmentDbContext(
    DbContextOptions<FulfilmentDbContext> options,
    ICompanyContext company,
    ILogger<FulfilmentDbContext> logger)
    : ModuleDbContext<FulfilmentDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("fulfilment");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
