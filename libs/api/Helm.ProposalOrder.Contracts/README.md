# Helm.ProposalOrder.Contracts

The public surface of the ProposalOrder module — the ONLY thing other modules may reference (ADR-001).
References `Helm.Core` only.

- `Dtos/` — data returned by this module's Contracts interfaces and APIs. Records, never EF entities.
- `Events/` — domain events (`IDomainEvent`), past-tense fact names (e.g. `ProposalOrderFinalized`).
  Catalog them in `EVENT-CATALOG.md`.
- `Interfaces/` — synchronous read interfaces (e.g. `IProposalOrderService`) other modules call.
  Batch-first: expose `...ByIds(IReadOnlyCollection<Guid>)`, never single-id calls used in loops.
