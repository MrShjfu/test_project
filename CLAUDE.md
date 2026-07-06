# NTG Platform (Helm)

Unified sailmaking platform for North Technology Group, replacing three legacy systems (North Sails CS, Quantum QES, Doyle GOP). One deployment serves all NTG brands and licensees. Phase 1 milestone: Doyle cutover.

## Architecture in one paragraph

A **.NET modular monolith** (`Helm.Host`) with ~10 vertically-sliced domain modules — CRM, CPQ, Proposal & Order, **Design Engine (core IP, built first)**, Planning, Inventory, Pre-Processing, Manufacturing, Fulfilment, After-Sales — in an **Nx + nx-dotnet monorepo** with three React frontends sliced by audience (Internal Platform, Customer Portal, Factory Kiosk offline PWA), each behind its own **BFF** (no API gateway). PostgreSQL with a schema per module, `IEventBus` + transactional outbox for async messaging, Hangfire for background jobs, hosted on Azure Container Apps + Static Web Apps, IaC in Bicep.

## Documentation map

| Doc | Read when |
| --- | --- |
| [docs/architecture/engineering-rules.md](docs/architecture/engineering-rules.md) | **Always** — the enforceable rules checklist for any code change |
| [docs/architecture/overview.md](docs/architecture/overview.md) | Full architecture: module map, repo layout, CQRS, CI/CD, infra, runbook, NFRs, glossary |
| [docs/adr/](docs/adr/) | ADR-001…011 — decisions of record with context, rationale, alternatives |

Source of truth is Confluence space **NTGHELM** (each file carries its source URL); these copies were exported 2026-07-06.

## Hard rules — never violate, even if asked to "just make it work"

1. **Module isolation**: modules reference other modules' `*.Contracts` only, never implementations; only `Helm.Host` references implementations. No cross-schema SQL/JOINs/FKs — cross-module refs are application-level ids. (ADR-001/002)
2. **Reads sync, writes async**: read another module via its Contracts interface (use batch variants, never single-id calls in loops); trigger work elsewhere via domain events. One business operation writes to exactly one module's schema — no distributed transactions; multi-module flows are sagas with compensation. (ADR-001/004)
3. **Events**: only via `IEventBus` (never a broker SDK), always through the module's outbox table in the same transaction as the state change. Consumers are idempotent (at-least-once delivery). Names are past-tense facts (`DesignFinalized`); evolve additively, breaking change = new event type. (ADR-004)
4. **Multi-company isolation**: every company-owned row carries `company_id`; every query is scoped by it at the repository base class. Only NTG-group `*:admin` may cross companies, and that path is audit-logged. A leak here is the project's worst-case bug. (ADR-003)
5. **Migrations are backward-compatible** (add nullable → backfill → enforce; never drop a column while code uses it), per-module, never touching another module's schema. (ADR-002)
6. **Stateless app tier**: no in-process session/file/consensus-cache state — Redis, Blob Storage. Background work must run once across replicas and be idempotent. (ADR-005/011)
7. **Contracts, not entities**: APIs return DTOs from Contracts, never EF entities. Frontends only use the OpenAPI-generated TS client (`libs/web/api-client`) — never hand-written DTOs. (ADR-009)
8. **BFFs compose and shape only** — business rules live in modules. Frontend feature libs (`libs/web/feature-*`) never import each other. (ADR-008)
9. **Secrets in Key Vault** via Managed Identity — never in config files or code. Every endpoint authorized by default. (ADR-003)

## Conventions

- API: `/api/v1/…`, RFC 7807 Problem Details + `traceId`, `{ items, totalCount }` pagination envelope, `Idempotency-Key` on writes, `202` + polling for long-running ops.
- Testing: xUnit + Testcontainers + `WebApplicationFactory` + NetArchTest (build-breaking) on backend; Vitest/RTL + Playwright (incl. Kiosk offline) on frontend; TS strict, no `any`; Sonar gate blocks merge. New event subscriber ⇒ idempotency test scenario. New company-scoped query ⇒ isolation test.
- Monorepo: use `nx affected` for build/test/lint; a backend contract change and its frontend fallout ship in the same PR.
- Performance budgets (p95): id-read 50ms, list 100ms, write 100ms (+side effects 200ms), aggregation 500ms via read models — don't join across schemas for dashboards, use CQRS projections.
- Extraction to microservices only when ≥2 ADR-001 triggers fire — don't design speculative service seams.

## When making design decisions

Check whether an ADR already answers the question before proposing an approach; cite it. If a change would contradict an ADR (e.g. introduce a gateway, share a schema, publish without outbox), stop and flag it as an architecture change requiring an ADR update — do not implement it quietly.
