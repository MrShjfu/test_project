using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Helm.Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Crm.Infrastructure;

public class CrmDbContext(
    DbContextOptions<CrmDbContext> options,
    ICompanyContext company,
    ILogger<CrmDbContext> logger)
    : ModuleDbContext<CrmDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("crm");

        b.Entity<Customer>(e =>
        {
            e.ToTable("customer");
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired();
            e.Property(c => c.Email).IsRequired();
        });

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
