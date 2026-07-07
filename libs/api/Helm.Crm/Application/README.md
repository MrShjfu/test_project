# Application

`CrmService` implements the `ICrmService` Contracts interface — the only way other modules read
CRM data (ADR-001/004: reads are synchronous, via Contracts, batch-first). Endpoints in `Api/`
call this same service (or `CrmDbContext` directly for writes) rather than duplicating query
logic.

Rules:
- Every read is `AsNoTracking`.
- Multi-id reads use a single `WHERE id IN (...)` batch query — never a loop of single-id calls
  (engineering-rules §2).
- Returns DTOs from `Helm.Crm.Contracts`, never EF entities (ADR-009).
