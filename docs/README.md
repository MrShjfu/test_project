# NTG Platform (Helm) — Design Documentation

Exported from Confluence space [NTGHELM](https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM) on 2026-07-06. Confluence remains the source of truth; each file carries its source URL. Re-export with the `confluence` skill if pages change.

## Contents

- [architecture/engineering-rules.md](architecture/engineering-rules.md) — distilled, enforceable rules checklist (start here for any code change)
- [architecture/overview.md](architecture/overview.md) — NTG Platform Architecture Overview (full document, 12 diagrams)

### Decisions of record (ADRs)

| ADR | Decision | Status |
| --- | --- | --- |
| [ADR-001](adr/ADR-001-modular-monolith.md) | Modular monolith architecture | Ready for review |
| [ADR-002](adr/ADR-002-postgresql-per-module-schemas.md) | PostgreSQL, schema per module | Ready for review |
| [ADR-003](adr/ADR-003-single-tenant-sso-rbac.md) | Single Azure tenant, Entra SSO, RBAC | Ready for review |
| [ADR-004](adr/ADR-004-async-messaging-and-outbox.md) | `IEventBus` abstraction + transactional outbox | Ready for review |
| [ADR-005](adr/ADR-005-background-jobs-hangfire.md) | Background jobs — Hangfire | Ready for review |
| [ADR-006](adr/ADR-006-dynamics-365-finance-only.md) | Dynamics 365 as finance integration only | Deferred (Phase 2, North) |
| [ADR-007](adr/ADR-007-data-migration-strategy.md) | Data migration — clean start + selective ETL | Principles accepted; detail per cutover |
| [ADR-008](adr/ADR-008-bff-per-frontend.md) | BFF per frontend, not an API gateway | Ready for review |
| [ADR-009](adr/ADR-009-monorepo-nx.md) | Monorepo — Nx + nx-dotnet | Ready for review |
| [ADR-010](adr/ADR-010-hosting-azure-container-apps.md) | Hosting — Azure Container Apps (not AKS day 1) | Ready for review |
| [ADR-011](adr/ADR-011-scaling-and-multi-region.md) | Scaling & multi-region — active-passive DR | Ready for review |
