# 01 — CI/CD Architecture

## Platform decision

**Azure DevOps end-to-end**: Azure Repos (code + PR), Azure Pipelines (CI/CD, YAML), Azure Boards (optional work-item links), Azure Artifacts/ACR (artifacts). Decided in the 2026-07-08 design session; the project starts fresh on Azure Repos. All CI logic lives in nx targets so the pipeline file (`azure-pipelines.yml`) stays a thin shell (ADR-009 discipline).

Diagrams (pipeline flow + environment topology): [cicd-architecture.drawio](cicd-architecture.drawio).

Architecture Overview §10 listed Azure Pipelines as unapproved; this document is the approval record. Flag to the team that overview §10 should be updated to reference this folder.

## Tooling map

| Layer | Tool | Notes |
| --- | --- | --- |
| Code + PR | Azure Repos (Git) | Branch policies, build validation |
| CI/CD | Azure Pipelines, YAML multi-stage, MS-hosted `ubuntu-latest` | Docker available for Testcontainers |
| Registry | Azure Container Registry (per-environment-shared, one instance) | `:sha` immutable tags |
| IaC | Bicep + `az deployment sub/group create --what-if` | Decision record below |
| Quality gates | nx affected (lint/test/build), NetArchTest, Coverlet (60% on Helm.Core), SonarQube ADO extension, api-client drift check | Blocking on PR |
| API deploy | ACA revisions + traffic splitting (blue-green) via `az containerapp` | Rollback = traffic revert |
| FE deploy | SWA deployment task (3 apps) + SWA staging-slot swap | PR previews wired via pipeline |
| DB migration | EF Core migration bundles, pipeline job before traffic shift | ADR-002 backward-compat contract |
| Identity | Workload identity federation (OIDC) for the ADO→Azure service connection; Managed Identity + Key Vault at runtime | No long-lived secrets anywhere |
| Monitoring | App Insights + Log Analytics + Azure Monitor alerts | See [04-monitoring.md](04-monitoring.md) |
| Dependency updates | Renovate (ADO extension) | npm + NuGet, weekly schedule |

## Branch & PR flow

- `main` protected: PR required, ≥1 approval, build-validation = pipeline CI stage, optional work-item link. Squash merge.
- Every merge to `main` is deployable; Prod promotion is by **approval**, not by branch. `release/*` branches remain an option for hotfix tracks (documented, not default).

## Pipeline stages (`azure-pipelines.yml`)

```
CI          (PR + main): nx affected -t lint,test,build (Testcontainers, NetArchTest, Coverlet)
                         SonarQube analysis · api-client drift check · docker build (no push on PR)
Package     (main only): build+push api image :sha to ACR · publish web bundles + infra artifacts
DeployDev   (main auto): bicep what-if→apply (dev params) · EF migration bundles · ACA revision→100%
                         SWA deploy · smoke: /health 200 + one authorized round-trip
IntTests    (main auto): integration suite + Playwright e2e against Dev
DeploySIT   (auto after IntTests): same steps, sit params, smoke gate
DeployProd  (manual approval on ADO Environment "prod"):
                         bicep what-if output attached for the approver
                         migration bundles · new ACA revision at 0% → 10% → 100% traffic
                         (approval or clean-metrics gate between steps) · SWA slot swap
```

Caching: Pipeline Caching for npm + nx cache. Agents: MS-hosted; revisit self-hosted only if queue time or Testcontainers pull-time hurts (decision point, not day-1).

**Known issue to fix during port:** `nx affected --parallel=3` produced an MSB3030 copy race between .NET projects sharing dependency outputs (seen in the walking-skeleton verification). Mitigate in the pipeline with `--parallel=1` for the .NET-heavy `test` target or per-project `dotnet build` locking; track a proper fix (e.g. `"dependsOn"` build chains in project.json) in the backlog (CICD-07).

## Deploy strategy & rollback

- ACA **multiple-revision mode**. Each deploy = new revision at 0% traffic → smoke against the revision-specific URL → shift 10% → 100%. Prod keeps the previous revision active for 24h; rollback = `az containerapp ingress traffic set` back to it (seconds, no rebuild).
- Migrations run **before** traffic shift and must be backward-compatible (ADR-002: add nullable → backfill → enforce), so old + new revisions run side-by-side safely during the shift.
- Background work during shift: two revisions may both run outbox relays/Hangfire — safe by design (FOR UPDATE SKIP LOCKED, idempotent consumers, storage-level Hangfire locks). This invariant is load-bearing: never weaken it.
- SWA: deploy to staging environment slot → validate → swap.

## PR ephemeral environments

Per overview §10: on PR open, a pipeline job creates ACA app `pr-<id>` (image from the PR build) + a dedicated **database `pr_<id>` on the Dev Postgres Flexible Server** (cheap; dropped on PR close) + SWA preview for the FE. Messaging for PR envs — Phase 1 decision deferred with two candidates: (a) topic prefix `pr<id>-` on the Dev Service Bus namespace, (b) API-only PR envs on InMemory transport for UI demos. Decide when building CICD-30 (backlog).
Teardown pipeline triggers on PR completion/abandonment; a scheduled janitor job deletes orphans older than 7 days.

## IaC decision: Bicep (Terraform considered)

Bicep is the standing choice (already scaffolded in `infra/`, compiles clean). Rationale vs Terraform, evaluated 2026-07-07/08:

- Azure-only committed platform (Entra, ACA, SWA, Key Vault) — Terraform's multi-provider strength is unused.
- No state-file operations for a 2–3-engineer team (ARM is the source of truth); Terraform requires state storage, locking, and drift ops.
- Day-0 support for fast-moving ACA features; `azurerm` provider typically lags (AzAPI workaround = raw ARM in HCL).
- The expected NTG subscription restructure favors Bicep (redeploy) over Terraform (state surgery: `state mv`/re-import).
- Pipeline fit: `what-if` output as the Prod approval artifact is native and cheap.
- Known Bicep gap: Entra app registrations (3 FEs + BFF auth) — plan: Microsoft Graph Bicep extension spike, fallback idempotent `az ad` scripts in `tools/` (backlog CICD-21).
- Revisit trigger: an organization-wide Terraform mandate or a need to manage non-Azure providers by IaC.

## Azure Repos project setup (fresh start)

1. Create ADO organization/project (`NTG-Helm`), enable Repos/Pipelines/Environments/Boards.
2. Create the repo and push the codebase; verify `git rev-parse HEAD` parity with the source of truth.
3. Branch policies on `main`: min reviewers, build validation (bound to the CI stage once the pipeline exists), squash-only.
4. Wire Renovate (ADO extension) for npm + NuGet updates (CICD-10) and pipeline-driven SWA preview environments (CICD-30).


## Out of scope (tracked, deliberate)

AKS, multi-region/DR pipeline automation (ADR-011 is design-for-now), load testing, DAST/pentest automation, Sandbox/licensee environments, data-migration ETL pipelines (ADR-007 per-cutover projects).
