using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Core.MultiCompany;

public abstract class ModuleDbContext<TSelf> : DbContext where TSelf : DbContext
{
    protected ICompanyContext Company { get; }

    protected ModuleDbContext(DbContextOptions<TSelf> options, ICompanyContext company, ILogger<TSelf> logger)
        : base(options)
    {
        Company = company;
        // Fires once per context INSTANCE (unlike OnModelCreating, which EF runs once per
        // context TYPE due to model caching — logging there would miss most admin sessions).
        if (Company.IsGroupAdmin) CrossCompanyAuditLogger.LogBypass(logger, Company.CompanyId);
    }

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
    }

    public string CurrentCompanyId => Company.CompanyId;
    public bool BypassFilter => Company.IsGroupAdmin;

    private void StampCompanyId()
    {
        foreach (var e in ChangeTracker.Entries<CompanyOwnedEntity>().Where(e => e.State == EntityState.Added))
        {
            e.Entity.CompanyId ??= Company.CompanyId;
            if (e.Entity.CompanyId != Company.CompanyId && !Company.IsGroupAdmin)
                throw new InvalidOperationException(
                    $"Cross-company write denied: entity CompanyId '{e.Entity.CompanyId}' does not match context company '{Company.CompanyId}'.");
        }
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
