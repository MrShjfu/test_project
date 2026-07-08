# Helm — NTG Platform

Unified sailmaking platform for North Technology Group. **Walking skeleton complete** (2026-07-08): all 10 domain modules scaffolded, shared infrastructure proven end-to-end by the CRM→CPQ demo slice.

## Quickstart

```bash
docker compose up -d --wait        # Postgres 17 + RabbitMQ 4
npm ci                             # (pnpm migration tracked as CICD-26)
dotnet build Helm.sln
npx nx serve api                   # API on http://localhost:5000
npx nx serve web-internal          # Internal app on http://localhost:4200
```

Local auth: `dotnet user-jwts create --project apps/api/Helm.Host --claim company_id=doyle --claim module_role=crm:editor`

Full stack in containers (no SDK needed): `docker compose --profile full up --build -d --wait` → API :5000, web :4200.

## Common commands

| Command | Purpose |
| --- | --- |
| `dotnet test Helm.sln` | All backend suites (unit + Testcontainers integration + ArchTests) |
| `npx nx run-many -t lint,test,build` | Frontend + workspace targets |
| `npx nx affected -t lint,test,build --parallel=1` | CI entrypoint (parallel=1: see CICD-07) |
| `npx nx g helm-module <Name> --schema <schema>` | Generate a new compliant domain module |
| `npm run build:generators` | Rebuild generator.js after editing generator.ts |
| `bash tools/scripts/generate-api-client.sh` | Regenerate the OpenAPI TS client (CI enforces drift) |
| `npx nx e2e web-internal-e2e` | Playwright smoke |
| `az bicep build --file infra/main.bicep` | Compile-check IaC |

## Modules (10 — folders, never services; ADR-001)

CRM · CPQ · ProposalOrder · Design★ · Planning · Inventory · PreProcessing · Manufacturing · Fulfilment · AfterSales — each: `Helm.<Mod>` + `Helm.<Mod>.Contracts` + `Helm.<Mod>.Tests`, own Postgres schema with outbox/processed_events, registered in `Helm.Host`.

## Architecture rules

Read `CLAUDE.md` (hard rules) and `docs/architecture/engineering-rules.md` before any change. Cross-module: read via `*.Contracts` (batch), write via events (outbox + `IEventBus`), never cross-schema SQL. Every company-owned row is `company_id`-scoped at the DbContext base.

## Docs

- `docs/architecture/` + `docs/adr/` — architecture of record (Confluence NTGHELM export; git-ignored, kept local)
- `docs/cicd/` — CI/CD & DevOps design (Azure DevOps target), environments Dev/SIT/Prod, task backlog CICD-01…31, diagrams
- `docs/superpowers/specs|plans/` — walking-skeleton spec + implementation plan
