# 02 — Environment Management

## Environment model (Phase 1)

| Env | Purpose | Deploy trigger | Data | Auth audience |
| --- | --- | --- | --- | --- |
| **Local** | Developer machines | — (docker compose + `nx serve`) | Throwaway volume | `dotnet user-jwts` |
| **PR-`<id>`** | Review a PR live | PR opened/updated | `pr_<id>` database on Dev server, synthetic seed | Dev Entra app (or dev JWTs until Entra lands) |
| **Dev** | Latest `main`, integration testing target | Auto on merge to `main` | Synthetic + seeded reference data | Entra dev app registration |
| **SIT** | System integration testing — stable build for QA/stakeholders | Auto after Dev integration tests pass | Anonymized/synthetic, refreshable | Entra SIT app registration |
| **Prod** | Doyle live | Manual approval | Real | Entra production apps (B2B + B2C) |

Growth path (per SoW/overview §10): Sandbox and a separate Staging/UAT can be added later as pure configuration — new Bicep param file + new pipeline stage; nothing structural changes. SIT plays the UAT role for Phase 1.

## Azure layout & naming

- One subscription (until the NTG restructure says otherwise); **one resource group per environment**: `rg-helm-dev`, `rg-helm-sit`, `rg-helm-prod` (+ transient `rg-helm-pr` holding PR apps, or PR apps inside `rg-helm-dev` — default: inside dev RG, simpler RBAC).
- Resource names: `helm-<resource>-<env>` (matches `infra/main.bicep` conventions), e.g. `helm-aca-env-sit`, `helm-pg-prod`, `helm-kv-prod`, `helm-sb-dev`, `swa-helm-internal-dev`.
- Shared across envs: **one ACR** (`acrhelm`) — images are env-agnostic (`:sha`), config is not baked in; **one Log Analytics per env** (cost isolation + retention differences).
- RBAC: pipeline service principal (workload identity federation) gets Contributor scoped per RG; humans get Reader on Prod by default (break-glass documented in runbook).

## Configuration matrix

Config flows from three sources, strictly layered (nothing else):

1. **Bicep parameters** (`infra/params/<env>.bicepparam`) — infra shape: SKUs, replica min/max, HA on/off, retention days.
2. **App settings** (set by Bicep on the ACA app) — non-secret runtime config: `ASPNETCORE_ENVIRONMENT`, `Messaging__Provider`, feature flags, App Insights connection string.
3. **Key Vault references** — all secrets: Postgres connection string, Service Bus connection (until Managed-Identity auth for SB is wired), external API keys. Runtime access via Managed Identity only (ADR-003).

| Setting | Local | PR | Dev | SIT | Prod |
| --- | --- | --- | --- | --- | --- |
| `Messaging__Provider` | RabbitMQ (compose) / InMemory (tests) | InMemory or SB-prefixed (open decision, 01 §PR envs) | AzureServiceBus | AzureServiceBus | AzureServiceBus |
| Postgres | compose container | `pr_<id>` db on dev server | Flexible Server B1ms | B1ms/B2s | B2s+, zone-redundant HA (overview §11) |
| ACA replicas | — | 0–1 (scale-to-zero) | 0–2 (scale-to-zero) | 1–3 | 2–10, no scale-to-zero |
| `Outbox:RelayEnabled` | true (false in test factories) | true | true | true | true |
| Hangfire dashboard | open via dev admin JWT | admin only | admin only | admin only | admin only + IP restriction (decision point) |
| App Insights sampling | off | off | 100% | 100% | adaptive |
| Log retention | — | 7d | 30d | 30d | 90d (compliance TBC) |

Notes:
- `AzureServiceBus` transport is currently `NotImplementedException` in `Helm.Core.Messaging` — implementing it is a prerequisite for Dev on ACA (backlog CICD-13). Until then Dev can run the RabbitMQ container option, but the target state is Service Bus per ADR-004.
- Every environment gets the full 10-module monolith — modules are folders, not deployables (ADR-001); there is exactly one `api` container app per env plus the SWA trio.

## Promotion rules

- Artifact promotion, not rebuild: the **same image digest** built in Package deploys to Dev → SIT → Prod. Bicep templates identical across envs; only param files differ.
- An env is "green" when: what-if applied cleanly, migrations succeeded, smoke passed, alert rules loaded. The pipeline records these as environment checks in ADO Environments.
- Data never flows up (Prod → SIT refreshes are an explicit, anonymizing runbook — not automated in Phase 1).

## Environment drift & hygiene

- All changes via pipeline; portal changes are break-glass only and must be codified into Bicep within 48h (drift check: scheduled weekly `what-if` run against each env alerts on non-empty diff — backlog CICD-22).
- PR janitor: scheduled cleanup of PR apps/databases older than 7 days (CICD-31).
