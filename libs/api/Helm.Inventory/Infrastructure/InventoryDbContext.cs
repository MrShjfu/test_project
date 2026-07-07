using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Inventory.Infrastructure;

public class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ICompanyContext company,
    ILogger<InventoryDbContext> logger)
    : ModuleDbContext<InventoryDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("inventory");

        // No entities yet — add this module's aggregates here (see Domain/README.md).

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
