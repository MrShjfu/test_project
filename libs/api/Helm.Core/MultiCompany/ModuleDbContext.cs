using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Core.MultiCompany;

public abstract class ModuleDbContext<TSelf>(
    DbContextOptions<TSelf> options, ICompanyContext company, ILogger<TSelf> logger)
    : DbContext(options) where TSelf : DbContext
{
    protected ICompanyContext Company { get; } = company;

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes()
                     .Where(t => typeof(CompanyOwnedEntity).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType);
            var body = Expression.OrElse(
                Expression.Property(Expression.Constant(this), nameof(BypassFilter)),
                Expression.Equal(
                    Expression.Property(p, nameof(CompanyOwnedEntity.CompanyId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentCompanyId))));
            et.SetQueryFilter(Expression.Lambda(body, p));
            et.GetProperty(nameof(CompanyOwnedEntity.CompanyId)).SetMaxLength(64);
        }
        if (Company.IsGroupAdmin) CrossCompanyAuditLogger.LogBypass(logger, Company.CompanyId);
    }

    public string CurrentCompanyId => Company.CompanyId;
    public bool BypassFilter => Company.IsGroupAdmin;

    private void StampCompanyId()
    {
        foreach (var e in ChangeTracker.Entries<CompanyOwnedEntity>().Where(e => e.State == EntityState.Added))
            e.Entity.CompanyId ??= Company.CompanyId;
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampCompanyId();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        StampCompanyId();
        return base.SaveChanges();
    }
}
