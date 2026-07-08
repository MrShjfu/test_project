# Helm Repo Scaffold — Design Spec

**Date:** 2026-07-06
**Status:** Approved by Vu Lam (brainstorming session)
**Governing docs:** [CLAUDE.md](../../../CLAUDE.md), [engineering-rules.md](../../architecture/engineering-rules.md), [overview.md](../../architecture/overview.md), [ADR-001…011](../../adr/)
**Discovery input:** Confluence NTGHELM [Module Findings Overview](https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM/pages/786679) (page 786679)

## Goal

Scaffold the Helm monorepo as a **walking skeleton**: every architectural rule in the ADRs exists as *running, tested code*, every one of the 10 domain modules exists as a standards-compliant skeleton, and exactly one thin demo slice (CRM → CPQ) proves the shared infrastructure end-to-end. This repo becomes the base Phase 1 (Doyle) modules are built on.

**Success criteria:** *(all verified 2026-07-08, final sweep)*

1. ✅ `docker compose up -d && nx serve api` gives a running API against local Postgres + RabbitMQ. *(also full-container profile: /health 200, web 200)*
2. ✅ The demo slice passes three integration tests (outbox end-to-end + idempotency, company isolation + admin audit, transaction rollback = no phantom event). *(4 tests, real RabbitMQ + Postgres)*
3. ✅ `Helm.ArchTests` fails the build on any module-boundary violation, automatically covering all current and future modules. *(bite-check evidenced in Task 12; scratch module auto-discovered: 33→36 tests)*
4. ✅ `nx g helm-module` generates a new compliant module with no manual wiring beyond what the generator does. *(scratch cycle: generate → build 0 errors → ArchTests green → clean revert)*
5. ✅ CI runs `nx affected -t lint,test,build` green (parallel=1 pending CICD-07) and fails on api-client drift; coverage gate 60% on Helm.Core (baseline ~71.7%). *(GitHub Actions interim; ADO pipeline per docs/cicd supersedes on the Azure Repos move)*

## Decisions of this spec

| Decision | Choice | Notes |
| --- | --- | --- |
| Scaffold depth | Walking skeleton, all 10 modules as compliant skeletons | not structure-only, not Phase-1 implementation |
| Demo slice | CRM `Customer` → `CustomerCreated` → CPQ consumer | the only domain code in the skeleton |
| Build order | **Generator-first (approach A)**: hand-build Core + CRM → distill `helm-module` generator from CRM → generate the other 9 → add demo slice | uniformity; generator is tested by generating 9 modules |
| Frontends | All 3 apps + 3 BFF projects | 3-app split is still a product-discovery working assumption; skeletons are thin so redirection is cheap |
| .NET | **.NET 10 (LTS)**; task 1 of the plan is an nx-dotnet spike | fallback if the plugin fails on .NET 10: `project.json` + `run-commands` executors wrapping `dotnet` CLI (keeps `nx affected`, loses plugin codegen). Spike outcome gets recorded in this spec + flagged to ADR-009 |
| Frontend stack | React 19 + Vite, TypeScript strict, Node 22, npm | |
| CI | GitHub Actions (`.github/workflows/ci.yml`) | overview §10 says Azure Pipelines but that is unapproved; CI logic lives in nx targets so the pipeline file is a thin shell, cheap to port |
| OpenAPI | **One OpenAPI doc per BFF** (3 total); `libs/web/api-client` has `internal/`, `portal/`, `kiosk/` subpaths | resolves the open question "3 BFFs → how many specs"; skeleton only generates `internal` (others have no endpoints yet) |
| Local dev | docker-compose: default profile = infra only (Postgres 17, RabbitMQ 4-management); `--profile full` adds containerized `api` + `web-internal` | day-to-day dev runs apps via `dotnet`/`nx` for DX; `full` profile = SDK-less demo. The Helm.Host Dockerfile written here is the same image ACA uses later (ADR-010) |

## Repo layout

Repo root = this repo (`test_project/`), solution `Helm.sln`, Nx workspace at root.

```
├── nx.json  package.json  tsconfig.base.json  Helm.sln
├── .github/workflows/ci.yml
├── docker-compose.yml  .env.example
├── apps/
│   ├── api/                      # Helm.Host — the ONLY project referencing module implementations
│   ├── web-internal/  web-portal/  web-kiosk/
├── libs/
│   ├── api/
│   │   ├── Helm.Core/  Helm.Core.Tests/
│   │   ├── Helm.ArchTests/
│   │   ├── Helm.Bff.Internal/  Helm.Bff.Portal/  Helm.Bff.Kiosk/
│   │   └── Helm.<Mod>/  Helm.<Mod>.Contracts/  Helm.<Mod>.Tests/   (×10)
│   └── web/
│       ├── shared-ui/  api-client/   # api-client is GENERATED — never hand-edited
│       └── feature-crm/               # demo page only
├── tools/generators/helm-module/
├── infra/                             # Bicep skeleton; must pass `bicep build`
└── docs/                              # architecture docs + this spec
```

Modules and schemas: `Crm/crm`, `Cpq/cpq`, `ProposalOrder/proposal_order`, `Design/design`, `Planning/planning`, `Inventory/inventory`, `PreProcessing/pre_processing`, `Manufacturing/manufacturing`, `Fulfilment/fulfilment`, `AfterSales/after_sales`.

Not modules: **a02 Finance** (D365 is a deferred one-way integration, ADR-006); **a01 Customer Portal** (it is the `web-portal` frontend).

## Helm.Core (SharedKernel) — the real part

- **Messaging:** `IEventBus` with two real transports — InMemory (tests) and RabbitMQ (local/demo) — selected by `Messaging:Provider` config. `AzureServiceBus` enum value exists and throws `NotImplementedException` deliberately (implemented in the infra phase).
- **Outbox (ADR-004):** `OutboxWriter` writes the event row in the module DbContext's transaction; `OutboxRelay : IHostedService` polls with `SELECT … FOR UPDATE SKIP LOCKED`, publishes via `IEventBus`, marks processed, re-processes on startup. Table shape per ADR-004.
- **Idempotent consumers:** consumer base class + per-schema `processed_events` (`event_id UUID PRIMARY KEY`); insert + effect in one transaction; PK conflict = skip.
- **Multi-company (ADR-003):** `ICompanyContext` from claims; `CompanyOwnedEntity` base; EF global query filter on `company_id` in the base DbContext. The only bypass is the `*:admin` claim, and that path writes an audit log entry.
- **Auth:** JWT Bearer; authorized-by-default fallback policy; local tokens via `dotnet user-jwts` (no real Entra in this scaffold — config placeholders only); claims transformer hydrates `company_id`/`module_roles` from `core.users` with a 5-minute cache.
- **API conventions:** RFC 7807 Problem Details + `traceId`; `{ items, totalCount }` pagination envelope; per-module health checks aggregated at `/health`.
- **Hangfire (ADR-005):** server + PostgreSQL storage (`hangfire` schema); dashboard behind `*:admin`; exactly one recurring job — purge `outbox`/`processed_events` rows older than 30 days.
- **`core` schema:** `companies`, `users` tables + migrations owned by Helm.Core.

## Module skeleton (what the generator emits, ×10)

```
Helm.<Mod>/
├── Api/<Mod>Endpoints.cs          # Map<Mod>Endpoints(); minimal-API group /api/v1/<mod>
├── Application/  Domain/           # empty, with 1-line README each
├── Infrastructure/<Mod>DbContext.cs  # own schema; outbox + processed_events tables
│   └── Migrations/                 # initial migration
└── <Mod>Module.cs                  # services.Add<Mod>Module(config): DI, health check, outbox relay registration
Helm.<Mod>.Contracts/               # events/, dtos/, interfaces/ folders with naming conventions documented
Helm.<Mod>.Tests/                   # boots the module via WebApplicationFactory (1 smoke test)
```

`Helm.Host` calls each `Add<Mod>Module` + `Map<Mod>Endpoints`. The generator updates `Helm.sln`, Host registration, and Nx project config; `Helm.ArchTests` needs no per-module edits (reflection-discovers all `Helm.*` assemblies).

**ArchTests enforce (build-breaking):** module → other module's implementation forbidden; Contracts cycles forbidden; only Helm.Host references implementations; BFF projects reference `*.Contracts` only; module DbContexts stay in their own schema (migration files asserted by convention test).

## Demo slice (CRM → CPQ)

```
POST /api/v1/crm/customers  (JWT company=doyle)
  → one transaction: INSERT crm.customer + INSERT crm.outbox
  → OutboxRelay (SKIP LOCKED) → RabbitMQ → CustomerCreated
  → CPQ consumer, one transaction: INSERT cpq.processed_events + INSERT cpq.customer_ref
GET /api/v1/crm/customers?page=&pageSize=   → envelope, company-scoped
ICrmService.GetCustomer(id) / GetCustomers(ids)  # single + batch, per engineering-rules §2
```

CRM owns one aggregate: `Customer(id, company_id, name, email)`. CPQ owns only the consumer + `customer_ref` table (makes idempotency observable). Event follows conventions: past-tense name, listed in an event catalog file in `Helm.Crm.Contracts`.

**Required integration tests** (Testcontainers Postgres + RabbitMQ):

1. **Outbox e2e + idempotency:** POST → CPQ has exactly one `customer_ref`; force redelivery of the same event → still one.
2. **Company isolation:** company-A token cannot read company-B's customer (list + by-id); `*:admin` token can, and an audit log entry is written.
3. **No phantom events:** induced failure after the customer insert but inside the transaction → rollback leaves no `crm.customer` and no `crm.outbox` row.

## Frontends & BFFs

- BFFs are separate projects mounted by Host at `/bff/internal`, `/bff/portal`, `/bff/kiosk`. Internal BFF has one composition endpoint (`GET /bff/internal/customers` → `ICrmService`). Portal/Kiosk BFFs are empty shells with auth wiring.
- `web-internal`: app shell + auth stub + `feature-crm` page (list/create customer via generated client). `web-portal`: shell. `web-kiosk`: shell + PWA manifest + service-worker scaffold (no offline logic yet — kiosk offline sync is an open design question, deliberately out of scope).
- `shared-ui`: only what the demo page needs. Nx boundary rules: `feature-*` libs must not import each other (lint-enforced).

## Testing & CI

- Backend: xUnit + FluentAssertions + Testcontainers + `WebApplicationFactory`; ArchTests in `dotnet test`. Coverlet coverage, 60% threshold on Helm.Core only (empty skeletons exempt).
- Frontend: Vitest + RTL on `feature-crm`/`shared-ui`; ESLint + Prettier + TS strict (no `any`); Playwright: one smoke test on web-internal.
- `ci.yml`: `nx affected -t lint,test,build` on PR + main; a job regenerates the OpenAPI client and **fails on diff**; Husky + lint-staged pre-commit.
- SonarCloud: not wired (needs org setup) — placeholder comment in ci.yml; follow-up item.

## Out of scope (add when the first real need lands)

| Deferred | Add when |
| --- | --- |
| Redis (cache/session) | first hot read or real session need; decision flagged in open questions |
| Saga orchestrator (`core.saga_state`) | first multi-module workflow (likely `DesignFinalized`) |
| CQRS read model | first cross-module dashboard |
| Azure Service Bus transport | staging environment work (infra phase) |
| Real Entra ID (B2B/B2C) | first deployed environment; B2B spike with Doyle tenant is a Phase 1 action item |
| Idempotency-Key middleware | first client-retryable write beyond the demo |
| Kiosk offline sync/conflict design | kiosk feature work — needs its own spec |
| Bicep beyond compilable skeleton | infra phase |
| SonarCloud gate | org onboarding |
| Data-migration ETL | per-company cutover planning (ADR-007) |

## Open technical points recorded during design

1. nx-dotnet × .NET 10 spike result → update this spec + note on ADR-009. **Resolved 2026-07-06**: `@nx-dotnet/core@3.0.2` (latest published) declares peer dependency `nx >= 20.0.0 < 23.0.0`, conflicting with the workspace's `nx@23.0.1`; forcing the install with `--legacy-peer-deps` let `nx g @nx-dotnet/core:app` run but it left the project graph broken (generated `apps/api/Helm.Host/Api.Helm.Host.csproj` only in a temp directory, wired dangling references to the never-created workspace path, and `nx show projects`/`nx build` failed workspace-wide with ENOENT) — so the plugin was removed and Task 2 lands the **project.json + `nx:run-commands` fallback** instead; `npx nx build api` now runs `dotnet build` successfully.
2. Per-BFF OpenAPI decision (made here) should be reflected back into overview §3 / ADR-009 wording on "the" generated client.
3. The 3-frontend split remains a working assumption pending product discovery (overview §1) — skeleton keeps all three deliberately thin.
