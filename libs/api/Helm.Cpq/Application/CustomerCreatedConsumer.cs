using Helm.Core.Messaging;
using Helm.Core.MultiCompany;
using Helm.Core.Outbox;
using Helm.Cpq.Domain;
using Helm.Cpq.Infrastructure;
using Helm.Crm.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Cpq.Application;

/// <summary>
/// Consumes Crm's CustomerCreated event and materializes a local CustomerRef (ADR-001: read
/// another module's data via your own copy populated by its events, never a cross-schema join).
/// Idempotent by construction (IdempotentConsumer's processed_events insert-first ordering) —
/// redelivery of the same event id is a no-op.
///
/// Template for all background consumers: derive ICompanyContext from the event, never a global
/// constant (ADR-003). CpqDbContext's constructor requires an ICompanyContext (for the company
/// query filter/write guard), but a background consumer has no ambient HTTP request to resolve one
/// from — so ResolveDbContext below builds CpqDbContext itself with a FixedCompanyContext scoped to
/// THIS event's own CompanyId, never a fixed/shared company across all events.
/// </summary>
public class CustomerCreatedConsumer(IServiceScopeFactory scopes, IEventBus bus)
    : IdempotentConsumer<CpqDbContext, CustomerCreated>(scopes, bus)
{
    protected override Task HandleAsync(CustomerCreated evt, CpqDbContext db)
    {
        db.Add(new CustomerRef { CustomerId = evt.CustomerId, CompanyId = evt.CompanyId, Name = evt.Name });
        return Task.CompletedTask;
    }

    protected override CpqDbContext ResolveDbContext(IServiceScope scope, CustomerCreated evt) =>
        ActivatorUtilities.CreateInstance<CpqDbContext>(scope.ServiceProvider, new FixedCompanyContext(evt.CompanyId));
}
