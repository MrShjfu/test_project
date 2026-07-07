using Helm.Crm.Contracts;
using Helm.Crm.Contracts.Dtos;
using Helm.Crm.Domain;
using Helm.Crm.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Helm.Crm.Application;

public class CrmService(CrmDbContext db) : ICrmService
{
    public async Task<CustomerDto?> GetCustomer(Guid id, CancellationToken ct)
    {
        var c = await db.Set<Customer>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : ToDto(c);
    }

    public async Task<IReadOnlyList<CustomerDto>> GetCustomers(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);

        return customers.Select(ToDto).ToList();
    }

    private static CustomerDto ToDto(Customer c) => new(c.Id, c.Name, c.Email);
}
