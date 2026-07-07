using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.ProposalOrder.Infrastructure;

public class ProposalOrderDbContext(
    DbContextOptions<ProposalOrderDbContext> options,
    ICompanyContext company,
    ILogger<ProposalOrderDbContext> logger)
    : ModuleDbContext<ProposalOrderDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("proposal_order");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
