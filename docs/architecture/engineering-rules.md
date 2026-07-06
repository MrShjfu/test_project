# NTG Platform (Helm) — Engineering Rules

Distilled, enforceable rules from the [Architecture Overview](overview.md) and [ADRs 001–011](../adr/). This is the checklist to apply when writing or reviewing any code in this repo. Where a rule needs context or alternatives, follow the ADR link — the ADR is authoritative.

## 1. Module boundaries (ADR-001, ADR-002)

The system is a **modular monolith** (`Helm.Host`) with ~10 domain modules sliced vertically: CRM, CPQ, Proposal & Order, Design Engine (★ core IP), Planning, Inventory, Pre-Processing, Manufacturing, Fulfilment, After-Sales.

**Non-negotiable:**

1. A module may reference another module's `*.Contracts` project **only** — never its implementation project. Only `Helm.Host` (the composition root) references module implementations.
2. No circular dependencies between Contracts projects.
3. A module never queries another module's DB schema — no cross-schema SQL, no cross-schema JOINs, no DB foreign keys between module schemas. Cross-module references are application-level ids (INT/UUID); consumers must tolerate "referenced entity not found / inactive".
4. The shared `core` schema (companies, users, currencies) is read-only for all modules and owned by `Helm.Core`.
5. Keep `Helm.Core` (SharedKernel) small — auth middleware, logging, tracing, Problem Details, event-bus abstractions, common value objects. Alert if it exceeds ~2000 LOC.
6. Each module exposes one registration entry point (`services.AddCrmModule(config)`) that `Helm.Host` calls at startup.
7. These rules are **build-breaking**: NetArchTest architecture tests in CI (one test per rule) on the backend; Nx module-boundary lint on the frontend. New modules must be added to those tests.

**Internal layering** (Clean Architecture, dependencies point inward): Api → Application → Domain; Infrastructure → Application; Application → Contracts. Match layers to complexity — simple CRUD modules may be Api + Infrastructure only; rich domains (Design Engine, CPQ, Inventory) get the full set.

## 2. Cross-module communication (ADR-001, ADR-004)

Pick the mechanism by intent:

| Intent | Mechanism |
| --- | --- |
| **Read** another module's data now | Synchronous call via the owning module's Contracts interface (`ICrmService.GetBoatSpec(id)`) |
| **React** to a fact that happened | Async domain event via outbox + `IEventBus` |
| Aggregation / reporting across modules | CQRS read model (own schema e.g. `reporting`, event-fed, eventually consistent) |
| Ad-hoc analytics / BI | Read replica or warehouse ETL — never BI JOINs across module schemas |

**Non-negotiable:**

1. Cross-module **writes** always go through events — never a synchronous "reach in and write". A single business operation writes to exactly one module's schema.
2. Provide a **batch variant** alongside every single-id Contracts read (`GetCustomers(ids)`) — never call a single-id read in a loop (N+1).
3. No event-driven projections in the monolith phase (publishing events for side effects ≠ replicating a dataset). Direct Contracts reads are the default while sharing a process.
4. Multi-module workflows are **sagas** with compensation — no 2PC, no distributed transactions. Persist saga state (`InProgress`/`Compensating`/`Completed`/`Failed`), idempotency key per saga, retry with exponential backoff, compensate in reverse order, timeout → auto-compensate, alert on sagas stuck > 10 min. Custom lightweight orchestrator on top of `IEventBus`; MassTransit is the designated fallback if saga needs grow.

## 3. Events & messaging (ADR-004)

1. Publish/subscribe **only** through the `IEventBus` abstraction in `Helm.Core` — never against a broker SDK. Transport is config-only: in-memory (tests), RabbitMQ (local dev, Docker), Azure Service Bus (staging/prod).
2. Every event is written to the publishing module's own **outbox table** (`<schema>.outbox`) **in the same DB transaction** as the state change. A background relay (`IHostedService`) publishes with `SELECT … FOR UPDATE SKIP LOCKED` (multi-instance safe) and re-processes on startup.
3. Delivery is **at-least-once → every consumer must be idempotent**. Default pattern: a `processed_events` table (`event_id UUID PRIMARY KEY`) in the consumer's schema; insert `event_id` + apply the effect in one transaction; PK conflict = skip. Version-checked upserts may replace the table where the effect is a natural upsert (projections). Idempotency must appear as a test scenario in every subscribing module's spec.
4. Naming: past-tense facts, `<Aggregate><PastTenseVerb>` (`OrderPlaced`, `DesignFinalized`). Every published event type + its consumers is listed in an **event catalog** in the publishing module's Contracts project.
5. Versioning: events are immutable contracts — evolve **additively only** (optional fields). Breaking change = new event type (`OrderPlacedV2`); publish both until consumers migrate. Consumers ignore unknown fields.
6. Failures: bounded retry with exponential backoff (~5 attempts) → dead-letter queue → alert. DLQ messages are inspected and replayed, never dropped. Outbox rows unprocessed > 5 min raise an alert. Purge processed outbox rows and `processed_events` after 30 days (Hangfire job).
7. Expect 1–5s delivery lag — events are not for synchronous workflows.

## 4. Database (ADR-002)

1. PostgreSQL, one instance; **one named schema per module** (`crm`, `design`, `proposal_order`, …). JSONB is available and encouraged for design/manufacturing payloads.
2. Migrations: EF Core, per module, scoped to that module's schema; `core` owned by `Helm.Core`. No migration touches another module's schema.
3. Migrations are **backward-compatible only**: add nullable → backfill → enforce. Never drop a column until the code that used it is gone.
4. Never expose EF entities to clients — Contracts DTOs only.

## 5. AuthN/AuthZ & multi-company isolation (ADR-003)

1. Identity: Microsoft Entra ID SSO (JWT Bearer, validated every request). Doyle users are Entra B2B guests; Portal customers use Entra External ID/B2C.
2. Authorization data (`company_id`, `module_roles` like `cpq:editor`, `crm:viewer`) comes from `core.users`, **not the token** (cached ~5 min, invalidated on role change). Default model: if you can see it, you can change it; viewer roles are audit/reporting exceptions.
3. **Every company-owned row carries `company_id`** (North, Quantum, Doyle, NTG-group, licensees). The repository base class scopes **every** query by `company_id` — the single exception is an NTG-group admin (`company_id='ntg'`, role `*:admin`), and every such cross-company read is audit-logged.
4. Integration tests must assert no cross-company leakage; no non-NTG user can widen scope. Treat licensee data as isolated like any other company until the business rule is confirmed.
5. Background work runs under a **service identity** with an explicit `company_id` context passed by the enqueuing code — never ambient cross-company access.
6. Secrets only in Azure Key Vault via Managed Identity — never in config or code.

## 6. API standards (Overview §7)

1. Versioning in the URL (`/api/v1/…`); support the previous version ≥ 6 months; emit a `Sunset` header and log clients on old versions.
2. Errors: RFC 7807 Problem Details, always with `traceId`.
3. Pagination: `?page=&pageSize=&sortBy=&sortDir=`; envelope `{ items, totalCount }`.
4. Writes accept an `Idempotency-Key` header; cache `(key → response)` for 24h.
5. Long-running operations: `202 Accepted` + operation URL to poll → `303` to result.
6. Every endpoint is authorized — no anonymous endpoints by default.

## 7. Frontend & BFF (ADR-008, ADR-009)

1. Frontends slice by **audience** (working assumption: Internal Platform, Customer Portal, Factory Kiosk PWA-offline), backend slices by **domain**. One BFF per frontend; no API gateway. BFFs live in `apps/api` alongside the monolith.
2. A BFF **composes and shapes only** — business rules never leak out of modules into a BFF. Shared composition logic goes in module Contracts, not copied between BFFs.
3. Domain modules on the frontend are `libs/web/feature-*` libs — never separate apps, never separate BFFs. Feature libs must not import from each other; shared UI goes in `shared-ui`.
4. Frontends never hand-write DTOs: the backend OpenAPI spec generates the TypeScript client into `libs/web/api-client` in CI. A backend contract change must surface as a TS compile error, fixed in the same PR (monorepo, atomic cross-layer PRs).
5. TypeScript strict, no `any`. Route/module code-splitting per feature lib; Kiosk precaches via service worker and must work offline.

## 8. Background jobs (ADR-005)

1. Hangfire with PostgreSQL persistence (`hangfire` schema) for fire-and-forget-with-retry and scheduled jobs. The outbox relay is a separate `IHostedService`, not Hangfire.
2. Jobs must be **idempotent** (at-least-once execution) and must run **once across replicas** (Hangfire storage locks; or the web/worker split per ADR-011).
3. Job method signatures are a **public contract**: evolve additively, keep them in stable types; for a breaking change drain the queue or shim the old method (queued jobs serialize the method reference).
4. Named queues (`critical` for payment/D365 push, `default`, `low`) so low-priority backlog can't starve time-sensitive work. Failed jobs are retained for triage and alerting — never silently dropped.
5. Dashboard (`/hangfire`) restricted to authenticated `*:admin` users.

## 9. Scaling & statelessness (ADR-010, ADR-011)

1. The app tier is **stateless** — no in-process session, files, or caches that must agree: sessions → Redis/stateless JWT; files/PDFs → Blob Storage; shared caches → Redis with event-driven invalidation.
2. Hosting: Azure Container Apps (KEDA autoscale, revisions/blue-green, per-PR ephemeral apps) + Static Web Apps for frontends. No AKS until a module is extracted.
3. Multi-region posture: active-passive DR (Postgres geo-replica, GRS Blob, Front Door in front from day 1). `company_id` is the partition axis if data residency is ever required — this is the one retrofit-expensive decision.
4. All Azure resources in Bicep under `infra/` — nothing provisioned by hand.

## 10. Testing & quality gates (Overview §9)

| Concern | Tool |
| --- | --- |
| Backend unit/integration | xUnit + FluentAssertions |
| Real DB in tests | Testcontainers (PostgreSQL) |
| API tests | `WebApplicationFactory` |
| Architecture rules | NetArchTest (build-breaking) |
| Backend coverage | Coverlet, CI threshold (start 60%, raise) |
| FE unit/component | Vitest + React Testing Library |
| E2E | Playwright (must include Kiosk offline scenarios) |
| Lint/format | ESLint + Prettier; TS strict, no `any` |
| Quality gate | SonarQube/SonarCloud on every PR — blocks merge |

Also required: contract tests at module seams; idempotent-consumer test scenarios wherever a module subscribes to events; multi-company isolation tests. Pre-commit: Husky + lint-staged. Use `nx affected` — run only what changed.

## 11. Performance targets (Overview §14)

p95: read by id < 50ms; filtered list < 100ms; write < 100ms; write + side effects < 200ms (event async); complex aggregation < 500ms (serve from read model); long reports/nesting → async 202. FE page load p75 < 2s. Watch for N+1 (`pg_stat_statements`).

## 12. Evolution discipline (ADR-001, Overview §15)

Do not extract a module to a service until **≥ 2** triggers fire: merge conflicts > 50% of the module's PRs; deploy cadence divergence > 2×; 2+ teams on one module; traffic > 10× module average; monolith CI build > 15 min; hard tech constraint. Extraction is strangler-fig, and each consumed synchronous read must be redesigned per-read (network call with timeout/retry/circuit-breaker + cache, or event-fed projection) — see ADR-001 §On extraction.

## 13. Phasing & external systems (ADR-006, ADR-007)

- Phase 1 = **Doyle** cutover (no D365 dependency). North migration is Phase 2.
- Dynamics 365 is **finance reporting only**, one-way push (outbox → Hangfire `critical` queue), idempotency key per financial record, "pushed?" state on the source record (not in Hangfire), reconciliation job, finance-visible alert on exhausted retries. D365 never writes back. Helm owns all operational data.
- Data migration: Helm starts clean per company; one-time selective ETL (customers, boats, discounts, open orders, active production jobs); legacy stays read-only for history. ETL must be re-runnable/idempotent with a `legacy_id → helm_id` map.
