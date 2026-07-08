# 07 — Tooling Alternatives & Improvement Options

Per-layer comparison: the standing choice, credible replacements, and the **switch trigger** — the concrete condition under which replacing the tool becomes the right call. Rule of thumb inherited from the ADRs: prefer managed services and the smallest operational surface for a 2–3-engineer team; replace only on a named trigger, never for fashion (same discipline as ADR-001 §extraction triggers).

## Platform & delivery

| Layer | Standing choice | Alternatives | Trade-off summary | Switch trigger |
| --- | --- | --- | --- | --- |
| Code host + CI/CD | **Azure DevOps** (Repos + Pipelines) | GitHub + Actions · GitLab | GitHub: better OSS ecosystem, Dependabot/Copilot-native; GitLab: built-in registry/security scanning, self-host option. ADO: Entra-native, Environments approvals, Boards included | Org-wide mandate; or if NTG adopts GitHub Enterprise — port is cheap (logic lives in nx targets, thin YAML shell) |
| IaC | **Bicep** | Terraform (full analysis: [06](06-terraform-option.md)) · Pulumi | Pulumi: real languages (C# fits the team!) + state service; strong for complex logic, but adds SaaS/state ops like Terraform | Terraform: org mandate / non-Azure providers. Pulumi: if infra logic outgrows declarative templates (loops/conditionals everywhere) |
| Deploy model | **Pipeline push** (Azure Pipelines → ACA/SWA) | Argo CD · Flux (GitOps pull) · Octopus Deploy | Argo/Flux are **Kubernetes-only** — they cannot target ACA. Octopus: strong release dashboards, another license + server | Argo CD/Flux become relevant ONLY on AKS migration (ADR-010 extraction path); then pair with External Secrets Operator + Key Vault CSI. Until then: pipeline push + drift check (CICD-22) already gives the GitOps guarantees that matter (git = source of truth, reconcile alerting) |
| API hosting | **Azure Container Apps** | AKS · App Service | Recap of ADR-010: AKS = cluster ops for no benefit at this scale; App Service = weaker container/revision story | ≥2 module-extraction triggers fire (ADR-001) or need for service mesh/sidecars/DaemonSets |
| FE hosting | **Static Web Apps** | Azure CDN+Storage · Cloudflare Pages | SWA: free PR previews, staging slots, integrated auth. CDN+Storage: cheaper at scale, more wiring | SWA quota/feature ceiling (e.g. advanced WAF needs → Front Door + Storage) |

## Build & quality

| Layer | Standing choice | Alternatives | Trade-off summary | Switch trigger |
| --- | --- | --- | --- | --- |
| Monorepo orchestrator | **Nx** (+ hand-written project.json for .NET) | Turborepo · Bazel · Moon | Turborepo: simpler but JS-only (no .NET graph); Bazel: hermetic + polyglot but heavy learning/ops; Moon: young | Bazel only if build times explode at much larger scale; Turborepo never (loses the cross-language graph that justified ADR-009) |
| FE package manager | **pnpm** (CICD-26) | npm · yarn berry · bun | npm: slowest installs, largest node_modules; yarn PnP: compat friction; bun: fastest but young ecosystem | bun install when its lockfile/workspace story matures AND CI install time actually hurts |
| Static analysis | **SonarQube** (ADO extension) | GitHub CodeQL (needs GitHub) · NDepend (.NET-deep, license) · Qodana | Sonar: broad, PR-gate integration, both stacks in one | CodeQL only if moving to GitHub; NDepend if architecture-debt metrics become a management KPI |
| E2E | **Playwright** | Cypress · Selenium | Playwright: faster, better parallelism, .NET binding exists, offline-PWA testing OK | No realistic trigger — Playwright is the current best-in-class |
| Dependency updates | **Renovate** | Dependabot (GitHub-only) · manual | Renovate: monorepo-aware grouping, pnpm+NuGet in one tool | Dependabot only if moving to GitHub |

## Data & messaging

| Layer | Standing choice | Alternatives | Trade-off summary | Switch trigger |
| --- | --- | --- | --- | --- |
| Database | **PostgreSQL Flexible Server** (ADR-002) | Azure SQL / SQL MI · CockroachDB · Citus (hyperscale) | ADR-002 already litigated SQL Server (cost, JSONB). Cockroach/Citus: horizontal write scaling | Only if single-writer Postgres becomes the bottleneck (ADR-011 explicitly accepts it isn't at NTG scale); Citus if one giant tenant ever dwarfs the rest |
| Message transport | **Azure Service Bus** (prod, via IEventBus) | RabbitMQ (self-host — already a supported transport) · Kafka / Event Hubs · NATS | Kafka/Event Hubs: log-based, replay, stream processing — but Helm's traffic is commands/facts at modest volume, not streams | Event Hubs/Kafka only if event **replay/stream analytics** becomes a requirement (e.g. design-telemetry pipelines); RabbitMQ if leaving Azure — config-only swap thanks to IEventBus (ADR-004's payoff) |
| Background jobs | **Hangfire** (ADR-005) | Quartz.NET · Azure Functions (timer) · Temporal | Temporal: durable workflows — overlaps the future saga orchestrator decision (ADR-004 flagged MassTransit as fallback; Temporal is the heavyweight third option) | Revisit when the first real multi-module saga lands: custom orchestrator (default) vs MassTransit vs Temporal in one decision |
| Caching | (none yet — Redis planned in overview §13) | Azure Cache for Redis · Garnet (MS, Redis-compatible) · in-process only | Garnet: cheaper/faster Redis-compatible, self-host on ACA | Add when the first hot-read need lands (walking-skeleton spec deferred it); evaluate Garnet vs managed Redis then |

## Secrets & configuration

| Layer | Standing choice | Alternatives | Trade-off summary | Switch trigger |
| --- | --- | --- | --- | --- |
| Secrets | **Key Vault + Managed Identity** | HashiCorp Vault (self-host/HCP) · Infisical/Doppler · SOPS | Vault: dynamic DB creds + multi-cloud, at the cost of running/renting a critical HA system; SaaS managers: third party holding secrets Azure already holds | Vault only if multi-cloud or dynamic-credential leases become real requirements |
| Non-secret config & feature flags | (app settings via Bicep today) | **Azure App Configuration** (recommended add, CICD-27) · LaunchDarkly · OpenFeature+flagd | App Config: native .NET provider, per-label envs, Key Vault references, cheap. LaunchDarkly: richer targeting, real license cost | Add App Config when the first runtime-toggle need lands — likely per-company feature rollout (multi-company model makes this valuable early) |
| Secret rotation | manual (provision-time) | **Key Vault near-expiry events → rotation job** (CICD-28) · Vault dynamic secrets | Event-driven rotation keeps the managed stack; no new system | Before Prod go-live (security review item, ADR-003) |
| Connection auth | connection strings in KV | **Entra/MI auth for Postgres + Service Bus** (CICD-29) | Removes whole classes of secrets; Npgsql + SB SDK both support token auth | Do during CICD-13/14 implementation — cheapest when wiring is fresh |

## How to read this doc

Every "standing choice" traces to an ADR or the 2026-07-08 CI/CD design session; every alternative has a **named trigger**. If a trigger fires, open an ADR (or update [01](01-cicd-architecture.md)) rather than switching quietly — same governance as CLAUDE.md's "when making design decisions".
