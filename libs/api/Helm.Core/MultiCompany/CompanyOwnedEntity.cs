namespace Helm.Core.MultiCompany;

public abstract class CompanyOwnedEntity
{
    // stamped by ModuleDbContext on save; non-admin cross-company writes are rejected
    public string CompanyId { get; set; } = null!;
}
