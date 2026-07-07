using Helm.Core.Messaging;

namespace Helm.Crm.Contracts.Events;

/// <summary>
/// Published on POST /api/v1/crm/customers, in the same transaction as the Customer insert
/// (outbox pattern, ADR-004). Past-tense fact; evolve additively — a breaking change is a new
/// event type, not a mutation of this one.
/// </summary>
public record CustomerCreated(Guid EventId, Guid CustomerId, string CompanyId, string Name) : IDomainEvent;
