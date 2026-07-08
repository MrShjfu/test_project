# Monorepo Folder Structure Proposal

Status: Proposed — backend (`libs/api/*`) already implemented and reviewed on branch
`worktree-module-layered-split`, not yet merged. Frontend (`libs/web/*`, `apps/web/*`)
not yet built; this doc fixes the shape it should land in when it is, so it doesn't need
a second restructure later.

## Structure

```
Helm/ (repo root)
├── apps/
│   ├── api/
│   │   └── Helm.Host/                        # composition root — registers every module, no business logic
│   └── web/                                   # NOT YET BUILT — per CLAUDE.md's 3-frontend architecture
│       ├── internal/                          # Internal Platform (staff-facing)
│       ├── portal/                            # Customer Portal
│       └── kiosk/                             # Factory Kiosk (offline PWA)
│
├── libs/
│   ├── api/
│   │   ├── Helm.Core/                         # shared kernel: multi-company scoping, outbox, event bus, auth, API conventions
│   │   ├── Helm.Core.Tests/
│   │   ├── Helm.ArchTests/                    # build-breaking module-boundary + layering enforcement
│   │   │
│   │   ├── Helm.<Module>.Domain/              # entities/aggregates — depends on Core only
│   │   ├── Helm.<Module>.Application/         # services implementing Contracts — depends on Domain+Infrastructure+Contracts+Core
│   │   ├── Helm.<Module>.Infrastructure/      # EF DbContext + migrations — depends on Domain+Core
│   │   ├── Helm.<Module>.Api/                 # minimal-API endpoints + module DI registration — depends on all of the above
│   │   ├── Helm.<Module>.Contracts/           # DTOs, cross-module interfaces, domain events — depends on Core only
│   │   └── Helm.<Module>.Tests/               # WebApplicationFactory integration tests
│   │       ×10: Crm, Cpq, ProposalOrder, Design, Planning, Inventory,
│   │             PreProcessing, Manufacturing, Fulfilment, AfterSales
│   │
│   └── web/                                    # NOT YET BUILT
│       ├── backend-client/                     # OpenAPI-generated TS client (ADR-009) — the only way frontends touch the API
│       │                                        # (named to avoid reading as another "api" folder next to libs/api/)
│       ├── feature-<domain>/                   # one per business capability × per frontend — never import each other (ADR-008)
│       ├── ui/                                 # shared design-system components
│       └── bff/
│           ├── internal/
│           ├── portal/
│           └── kiosk/
│
├── tools/
│   └── generators/
│       └── helm-module/                        # Nx generator — scaffolds the 6-project module shape for new backend modules
│
├── docs/
│   ├── architecture/                           # ⚠️ referenced by CLAUDE.md, MISSING — overview.md, engineering-rules.md
│   ├── adr/                                     # ⚠️ referenced by CLAUDE.md (ADR-001…011), MISSING
│   └── superpowers/
│       ├── plans/
│       └── specs/
│
├── infra/                                       # ⚠️ Bicep IaC, referenced in the scaffold spec, MISSING
├── .github/workflows/                           # ⚠️ CI, referenced in the scaffold spec, MISSING
│
├── docker-compose.yml                           # postgres:17 + rabbitmq:4
├── Helm.sln / global.json / nx.json / package.json
```

## Dependency rules per backend layer

Enforced by `Helm.ArchTests` (Mono.Cecil-based assembly-reference checks), not just convention:

- `Domain → Core` only
- `Infrastructure → Domain, Core`
- `Application → Domain, Infrastructure, Contracts, Core`
- `Api → Domain, Application, Infrastructure, Contracts, Core`
- `Contracts → Core` only
- Cross-module reads go through another module's `Contracts` exclusively — never another
  module's `Domain`/`Application`/`Infrastructure`/`Api`.

## Decisions made in reaching this proposal

**Top level stays `apps/` + `libs/`, split by runtime underneath (`api/` vs `web/`), not
`client/` + `server/` at the root.**
- `apps/`/`libs/` are Nx's actual defaults (`nx.json` `workspaceLayout`) — using them
  means every Nx generator, the graph visualizer, and anyone who's used Nx before reads
  this layout for free, with no `workspaceLayout` override to explain.
- The backend split (`libs/api/*`, 64 projects) is already built and reviewed with every
  `.csproj` `ProjectReference`, every `project.json`, `Helm.sln`, and the generator
  templates hardcoding relative paths like `..\..\..\libs\api\Helm.Core\Helm.Core.csproj`.
  A `client/`/`server/` root split would touch every one of those paths for a rename with
  no functional benefit.
- Splitting by runtime (`api/` vs `web/`) under each of `apps/`/`libs/` — rather than by
  business domain (`libs/crm/api`, `libs/crm/web`) — matches this repo's reality: the
  `@nx-dotnet` plugin integration failed, so backend and frontend already build through
  completely separate command paths (`dotnet` CLI wrapped in `nx:run-commands` vs. native
  Nx JS tooling). Keeping them in siloed top-level trees avoids mixing `.csproj`/NuGet
  and `package.json`/npm concerns in the same directory (IDE solution filtering,
  `.gitignore` for `bin`/`obj` vs `node_modules`, CI job splitting).

**`libs/web/api-client` renamed to `libs/web/backend-client`.**
The original name read as ambiguous next to the top-level `libs/api/` tree — a skim of
top-level names could momentarily suggest `api-client` belongs to the backend rather than
being a frontend library that *calls* the backend. Only this one lib is renamed; nothing
else changes.

**Known gaps (`docs/architecture/`, `docs/adr/`, `infra/`, `.github/workflows/`) are not
new** — flagged in an earlier architecture-overview pass, carried over here for
visibility since they'll be more noticeable once `libs/web/*` brings more people into the
repo.

## Open question

None blocking — this doc fixes the target shape for when frontend work starts; the
backend half already matches it and is sitting reviewed on
`worktree-module-layered-split`, pending a merge/PR decision.
