using Helm.Core.MultiCompany;

namespace Helm.Cpq.Domain;

/// <summary>
/// Local read-side reference to a Crm Customer, populated by <see cref="Application.CustomerCreatedConsumer"/>
/// consuming Crm's CustomerCreated event (ADR-001: read another module's data via its own copy,
/// never a cross-schema join). Proof-of-infrastructure demo slice for Task 15 — Cpq's eventual
/// quoting workflows will need to reference a customer by id without calling into Crm synchronously.
/// </summary>
public class CustomerRef : CompanyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = null!;
}
