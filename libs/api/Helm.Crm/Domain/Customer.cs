using Helm.Core.MultiCompany;

namespace Helm.Crm.Domain;

public class Customer : CompanyOwnedEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
}
