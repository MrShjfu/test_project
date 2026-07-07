# Helm — Infrastructure (Bicep)

Skeleton IaC for the Helm platform. Compiles today; **deployment is infra-phase
work** (see below for exactly what's missing and why).

## Layout

```
infra/
  main.bicep                    # composes the modules below, one per environment
  modules/
    container-apps.bicep        # ACA managed environment + Helm.Host container app
    postgres.bicep               # PostgreSQL Flexible Server (burstable, db `helm`)
    servicebus.bicep             # Service Bus namespace + `helm-events` topic
    keyvault.bicep                # Key Vault, RBAC authorization
    static-web-apps.bicep        # 3 SWAs: web-internal, web-portal, web-kiosk
```

## Compile-check

No Azure login or deployment is required to validate syntax/types:

```bash
az bicep install        # one-time, user-local, no sudo
az bicep build --file infra/main.bicep
az bicep build --file infra/modules/container-apps.bicep
az bicep build --file infra/modules/postgres.bicep
az bicep build --file infra/modules/servicebus.bicep
az bicep build --file infra/modules/keyvault.bicep
az bicep build --file infra/modules/static-web-apps.bicep
```

Each command should exit `0` with no warnings. `main.bicep` requires the
`postgresAdminPassword` **secure** param at deploy time — it has no default,
so `az bicep build` (a compile step, not a deploy) never needs it.

## What this skeleton provides

Real, minimal resources for the five modules named in the architecture
(overview §11):

- **Container Apps**: one managed environment + one container app for
  `Helm.Host`, external ingress on port 8080, min/max replicas params,
  a Log Analytics workspace for `appLogsConfiguration` (ADR-010).
- **PostgreSQL Flexible Server**: burstable SKU (`Standard_B1ms` default), one
  database `helm`, zone-redundant HA gated to `env == 'prod'` (overview §11),
  admin password as a required `@secure()` param (ADR-002).
- **Service Bus**: Standard namespace + `helm-events` topic — the prod/staging
  `IEventBus` transport (ADR-004).
- **Key Vault**: RBAC-authorization enabled, no access policies — secrets
  reach the app via Managed Identity (ADR-003).
- **Static Web Apps**: three Free-tier SWAs, one per frontend
  (`web-internal`, `web-portal`, `web-kiosk`), matching the BFF-per-frontend
  split (ADR-008).

Per-environment naming follows `helm-<resource>-<env>` (e.g.
`helm-psql-staging`).

## What is deliberately NOT here (infra-phase work)

- **Deployment pipelines** — no GitHub Actions/Azure DevOps workflow, no
  parameter files per environment, no approval gates. This skeleton only
  proves the templates are valid; wiring CI to run `az deployment group
  create` is a separate, later task.
- **Container image build/push** — `helmHostImage` is a placeholder
  (`mcr.microsoft.com/dotnet/samples:aspnetapp`). Building and pushing the
  real `Helm.Host` image (and wiring an ACR + Managed Identity pull) is
  infra-phase work.
- **Key Vault references / secret wiring** — the container app's `envVars`
  param is an empty placeholder array. Wiring Postgres/Service Bus connection
  strings as Key Vault references, and granting the container app's Managed
  Identity `Key Vault Secrets User` via RBAC, is infra-phase work (ADR-003,
  ADR-010).
- **Azure Front Door** — ADR-011 calls for Front Door in front from day 1
  (even single-region) so a second region is a config change later, not a
  re-architecture. Not stood up here; this skeleton is single-region only.
- **Redis** — ADR-011/ADR-005 require stateless app tier (sessions/shared
  cache in Redis, not in-process). No Redis resource yet; add alongside the
  worker-app split when background-job single-execution is implemented.
- **DR / secondary region** — ADR-011's active-passive posture (PostgreSQL
  geo-replication read replica, GRS Blob Storage, Front Door health-based
  failover) is a deliberate later phase; this skeleton provisions one region.
- **Networking (VNet integration, private endpoints)** — none of the modules
  here are VNet-injected; that hardening is infra-phase work once an
  environment is actually deployed.
- **Blob Storage** — referenced in overview §11 for design files/CDN, not
  included in this skeleton (no module owns file storage requirements yet).

## Parameters worth knowing about

- `env` — `dev` | `staging` | `prod`, drives naming and the Postgres HA gate.
- `postgresAdminPassword` — `@secure()`, no default, must be supplied at
  deploy time (e.g. from a pipeline secret or Key Vault reference — not
  committed anywhere).
- `helmHostImage` — placeholder container image; replace with the built
  image reference in the deploy pipeline.
