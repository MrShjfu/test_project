# 05 — CI/CD Task Backlog

Ordered, phased backlog to stand up the full DevOps system. Each task has prerequisites (Pre) and done-criteria (DoD). IDs are stable for cross-referencing from the other docs. Effort: S ≤ 0.5d, M ≤ 2d, L > 2d.

## Phase A — Foundation (Azure DevOps + identity)

| ID | Task | Pre | DoD | Effort |
| --- | --- | --- | --- | --- |
| CICD-01 | Create ADO org/project `NTG-Helm`; enable Repos/Pipelines/Environments; add team with Entra identities | Azure tenant access | Project exists, members sign in via Entra | S |
| CICD-02 | Create the Azure Repos repository and push the codebase; devs switch remotes | CICD-01 | HEAD sha parity; team pushing to ADO | S |
| CICD-03 | Branch policies on `main`: ≥1 reviewer, build validation (placeholder until CICD-05), squash-only | CICD-02 | Direct push to main rejected | S |
| CICD-04 | Entra apps + **workload identity federation** service connections: `helm-dev`, `helm-sit`, `helm-prod` (RG-scoped), `helm-acr` | CICD-01, subscription | `az` deploy from a test pipeline succeeds per env; zero stored secrets | M |

## Phase B — CI pipeline

| ID | Task | Pre | DoD | Effort |
| --- | --- | --- | --- | --- |
| CICD-05 | Author `azure-pipelines.yml` CI stage: checkout fetch-depth 0, Node 22 + .NET 10 setup, corepack enable + `pnpm install --frozen-lockfile`, `pnpm nx affected -t lint,test,build` with Pipeline Caching (pnpm store + nx) | CICD-02, CICD-26 | PR build green end-to-end incl Testcontainers | M |
| CICD-26 | Migrate workspace npm → **pnpm**: pnpm-lock.yaml, `packageManager` field (corepack), remove package-lock.json, update scripts/docs, verify nx + husky + Playwright still green | CICD-02 | `pnpm install --frozen-lockfile` + full `nx run-many` green | M |
| CICD-06 | Port api-client drift gate (`tools/scripts/generate-api-client.sh` + `git diff --exit-code`) | CICD-05 | Intentional drift fails the PR | S |
| CICD-07 | Fix the `nx affected --parallel` MSB3030 copy race (proper project.json dependsOn chains or `--parallel=1` for dotnet targets, documented) | CICD-05 | 3 consecutive clean parallel runs | M |
| CICD-08 | Coverage gate: coverlet 60% line on `[Helm.Core]*` (already in Helm.Core.Tests.csproj from walking-skeleton Task 22 leftovers — verify + wire as pipeline step) | CICD-05 | Under-threshold build fails | S |
| CICD-09 | SonarQube: stand up SonarCloud-or-server decision, ADO extension, PR gate | CICD-05 | Sonar status on PR, merge blocked on red | M |
| CICD-10 | Renovate ADO extension with pnpm+NuGet presets, weekly schedule | CICD-02 | First Renovate PRs open | S |
| — | Also: finish walking-skeleton Task 22 leftovers (husky/lint-staged local hooks — currently uncommitted in the repo) | — | Hooks fire on commit | S |

## Phase C — Environments & deploy (Dev → SIT)

| ID | Task | Pre | DoD | Effort |
| --- | --- | --- | --- | --- |
| CICD-11 | `infra/params/{dev,sit,prod}.bicepparam` + extend modules to accept SKU/replica/HA/retention params per 02 §matrix | infra/ skeleton | `what-if` renders sensible diff per env | M |
| CICD-12 | Package stage: ACR push `:sha`, publish web bundles + infra artifact | CICD-04/05 | Immutable image per main commit | S |
| CICD-13 | **Implement AzureServiceBus transport for IEventBus** (currently NotImplementedException) + idempotent topology provisioning (topics per event type) | Helm.Core | RabbitMQ tests' equivalents green against SB (Testcontainers SB emulator or dev namespace) | L |
| CICD-14 | EF migration bundles: Package builds 12 bundles (core, hangfire owner, 10 modules); deploy step template runs them pre-traffic | CICD-11 | Fresh env migrates from zero; re-run is no-op | M |
| CICD-15 | `infra/modules/monitoring.bicep`: Log Analytics + App Insights + action group + alert catalog (04) | CICD-11 | Alerts visible in portal after deploy | M |
| CICD-16 | App telemetry: App Insights SDK in Helm.Host + custom samplers (outbox lag, DLQ depth, Hangfire failed) | CICD-15 | Metrics visible in App Insights | M |
| CICD-17 | FE telemetry: App Insights JS behind build flag | CICD-15 | Page views/errors in App Insights (dev) | S |
| CICD-18 | Workbook (RED per module) as code in `infra/workbooks/` | CICD-16 | Dashboard renders per env | S |
| CICD-19 | **Security gate before Entra tokens flow**: gate the claims-transformer dev backdoor on environment/issuer (Task 6 review finding); Entra app registrations for 3 FEs + BFF audiences (Graph Bicep spike, fallback `az ad` scripts) — includes CICD-21 | CICD-04 | Dev env rejects token-supplied company_id from non-dev issuer; login works on Dev | L |
| CICD-20 | DeployDev + IntTests stages: what-if→apply, bundles, ACA revision 100%, SWA deploy, smoke, then integration+Playwright vs Dev | CICD-12..14 | Merge to main lands on Dev green automatically | L |
| CICD-21 | (folded into CICD-19) Entra IaC spike | — | — | — |
| CICD-22 | Weekly drift check: scheduled what-if per env, alert on non-empty diff | CICD-20 | Portal hand-edit triggers alert | S |

## Phase D — Prod & release engineering

| ID | Task | Pre | DoD | Effort |
| --- | --- | --- | --- | --- |
| CICD-23 | ADO Environment `prod` with approvals; DeployProd stage: what-if artifact for approver, blue-green 0→10→100 traffic shift with metric gate, 24h fallback revision, SWA slot swap; **rollback runbook** (traffic revert + fix-forward schema policy) | CICD-20 | Game-day: deploy + forced rollback both < 5 min | L |
| CICD-24 | Release notes automation (ADO work items → release annotation in App Insights) | CICD-23 | Deploy markers visible on dashboards | S |
| CICD-25 | Availability test (Prod /health) + Teams action group wiring | CICD-15 | Test alert fires to Teams | S |

## Phase E — PR ephemeral environments

| ID | Task | Pre | DoD | Effort |
| --- | --- | --- | --- | --- |
| CICD-30 | PR env pipeline: ACA app `pr-<id>` + `pr_<id>` database on Dev server + SWA preview pointed at it; decide PR messaging option (SB prefix vs InMemory) — record decision in 01 | CICD-20 | PR comment carries live URLs | L |
| CICD-31 | Teardown on PR close + 7-day janitor for orphans | CICD-30 | No orphaned PR apps after a week of use | S |

## Sequencing summary

A (1→4) → B (5→10, parallelizable) → C (11/12 → 13/14/15 in parallel → 16..20 → 22) → D → E. Critical path to "Doyle can deploy": A → B5 → C11,12,14,20 → D23. CICD-13 (Service Bus transport) and CICD-19 (Entra) are the two long poles that can start early in parallel.

Diagrams: [diagrams/pipeline-flow.drawio](diagrams/pipeline-flow.drawio) · [diagrams/environments-monitoring.drawio](diagrams/environments-monitoring.drawio) · [diagrams/be-deploy-flow.drawio](diagrams/be-deploy-flow.drawio) · [diagrams/fe-deploy-flow.drawio](diagrams/fe-deploy-flow.drawio) · [diagrams/database-config-flow.drawio](diagrams/database-config-flow.drawio).
