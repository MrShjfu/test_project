using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Helm.Cpq.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Cpq.Infrastructure;

public class CpqDbContext(
    DbContextOptions<CpqDbContext> options,
    ICompanyContext company,
    ILogger<CpqDbContext> logger)
    : ModuleDbContext<CpqDbContext>(options, company, logger)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("cpq");

        b.Entity<CustomerRef>(e =>
        {
            e.ToTable("customer_ref");
            e.HasKey(c => c.CustomerId);
            e.Property(c => c.CustomerId).HasColumnName("customer_id");
            e.Property(c => c.CompanyId).HasColumnName("company_id");
            e.Property(c => c.Name).HasColumnName("name").IsRequired();
        });

        OutboxModelBuilder.AddOutbox(b);

        base.OnModelCreating(b); // must run last — applies the company query filter (ADR-003)
    }
}
