using Helm.Crm.Contracts.Dtos;

namespace Helm.Crm.Contracts;

/// <summary>
/// Cross-module read surface for the CRM module (ADR-001/004: reads are sync, via Contracts;
/// writes are async via events). Callers must use the batch overload when reading more than one
/// customer — never single-id calls in a loop (engineering-rules §2).
/// </summary>
public interface ICrmService
{
    Task<CustomerDto?> GetCustomer(Guid id, CancellationToken ct);

    Task<IReadOnlyList<CustomerDto>> GetCustomers(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
