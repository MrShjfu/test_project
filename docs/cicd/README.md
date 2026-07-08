# Helm — CI/CD & DevOps Documentation

**Status:** Design approved in brainstorming session 2026-07-08 (Vu Lam). Supersedes the unapproved platform note in Architecture Overview §10 (Azure Pipelines was listed there; this folder is the decision of record — see [01-cicd-architecture.md](01-cicd-architecture.md) §Platform).
**Scope:** Phase 1 (Doyle cutover) environments Dev · SIT · Prod + PR ephemeral environments, on Azure DevOps end-to-end.

| Doc | Contents |
| --- | --- |
| [01-cicd-architecture.md](01-cicd-architecture.md) | Platform decision (Azure DevOps + Azure Repos), pipeline stages, quality gates, deploy strategy (ACA blue-green), IaC decision (Bicep vs Terraform), Azure Repos project setup |
| [02-environments.md](02-environments.md) | Environment model (Dev/SIT/Prod/PR-ephemeral), resource-group & naming conventions, config matrix, environment promotion rules |
| [03-deployment-config.md](03-deployment-config.md) | Deployment configuration: Bicep parameter files, app settings & connection strings via Key Vault, ACA revision/traffic settings, EF migration bundles, SWA config |
| [04-monitoring.md](04-monitoring.md) | Observability stack: Application Insights, Log Analytics, alert catalog, dashboards, availability tests |
| [05-task-backlog.md](05-task-backlog.md) | **The work backlog** — phased, ordered tasks with prerequisites and done-criteria to stand the whole system up |
| [06-terraform-option.md](06-terraform-option.md) | Alternative: the same design on Terraform — state backend, pipeline deltas, backlog deltas, switch triggers & cost |
| [07-tooling-alternatives.md](07-tooling-alternatives.md) | Per-layer tool comparison: standing choice vs replacements (Argo CD/Flux, Vault, Kafka, Pulumi, App Configuration…) with named switch triggers |
| [diagrams/](diagrams/) | draw.io diagrams (one file per flow): [pipeline-flow](diagrams/pipeline-flow.drawio) · [environments-monitoring](diagrams/environments-monitoring.drawio) · [be-deploy-flow](diagrams/be-deploy-flow.drawio) · [fe-deploy-flow](diagrams/fe-deploy-flow.drawio) · [database-config-flow](diagrams/database-config-flow.drawio) · [module-map](diagrams/module-map.drawio) |

Related repo assets that already exist (walking-skeleton phase):

- `infra/` — Bicep skeleton (compiles; ACA, Postgres Flexible, Service Bus, Key Vault, 3 SWAs)
- `tools/scripts/generate-api-client.sh` — OpenAPI → TS client drift gate, reused as-is in the ADO pipeline
- `docker-compose.yml` — local environment (not managed by CI/CD)

Governing architecture docs: `CLAUDE.md` hard rules, `docs/architecture/engineering-rules.md`, ADR-002 (migrations), ADR-003 (identity/secrets), ADR-009 (monorepo/nx affected), ADR-010 (ACA+SWA hosting), ADR-011 (scaling/DR posture).
