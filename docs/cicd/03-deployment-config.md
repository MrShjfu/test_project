# 03 — Deployment Configuration

## Repository layout for deployment assets

```
infra/
├── main.bicep                    # exists (skeleton) — extended per backlog
├── modules/                      # aca, postgres, servicebus, keyvault, swa (exist)
│   └── monitoring.bicep          # NEW: app insights + log analytics + alerts (CICD-15)
├── params/
│   ├── dev.bicepparam            # NEW per-env parameter files (CICD-11)
│   ├── sit.bicepparam
│   └── prod.bicepparam
azure-pipelines.yml               # NEW: the multi-stage pipeline (CICD-05..09)
pipelines/                        # NEW: stage/step templates
├── templates/ci-steps.yml
├── templates/deploy-env.yml      # parameterized: env name, params file, approval-bound
└── templates/pr-env.yml          # create/teardown ephemeral env
tools/scripts/generate-api-client.sh   # exists — reused by CI drift gate
```

## Bicep parameters (per env)

`infra/params/<env>.bicepparam` carries exactly what differs per environment (see 02 §Configuration matrix): SKUs, replica bounds, HA flags, retention, sampling, allowed CORS origins, Entra app/client ids (ids only — never secrets). Everything not in a param file is intentionally identical across environments.

Secrets are **never** parameters. Provisioning-time secrets (Postgres admin password) are generated during provisioning and written straight into that env's Key Vault; pipeline and humans reference, never read-and-paste.

## Application configuration on ACA

Set by Bicep on the container app (per env):

- `ASPNETCORE_ENVIRONMENT` = Development (dev) / Staging (sit) / Production (prod) — note SIT maps to .NET's `Staging` so `IsDevelopment()`-gated features (OpenAPI docs, dev auth backdoor) are OFF outside Dev. **The claims-transformer dev backdoor (token-supplied `company_id`) must additionally be gated on environment before Entra tokens flow — tracked security item (CICD-19), origin: Task 6 review.**
- `ConnectionStrings__Helm` → Key Vault reference.
- `Messaging__Provider=AzureServiceBus`, `Messaging__ServiceBus__…` → Key Vault reference (or Managed Identity auth when implemented).
- `ApplicationInsights__ConnectionString` → from monitoring module output.
- `Outbox:RelayEnabled=true`, `Jobs:Enabled=true` (the test-only toggles default on).

Container: single image `acrhelm.azurecr.io/helm-host:<sha>`; port 8080; liveness/readiness probes on `/health`; CPU/memory per env params.

## Database migrations in the pipeline

- Package stage builds **EF migration bundles** (one per module — 10 module schemas + core + hangfire owner schemas): `dotnet ef migrations bundle --project libs/api/Helm.<Mod> --startup-project apps/api/Helm.Host --context <Mod>DbContext --self-contained`.
- Deploy stage runs bundles against the env database (pipeline step with the Key Vault-sourced connection string) **before** any traffic shift.
- Contract (ADR-002, non-negotiable): backward-compatible only — add nullable → backfill → enforce; never drop a column while any live revision uses it. The 24h-old Prod revision must always be able to run against the migrated schema.
- Rollback story: schema rolls **forward** only (fix-forward); app rolls back by traffic revert. This asymmetry is deliberate and documented in the runbook (CICD-23).
- PR envs: bundles run against the fresh `pr_<id>` database (full migrate from zero — also a nice continuous test that migrations apply cleanly from scratch).

## ACA revision & traffic configuration

- Revision mode: **multiple**; revision suffix = short sha.
- Deploy steps: create revision (0%) → smoke revision URL (`/health` + one authorized round-trip) → `az containerapp ingress traffic set` 10% → observe (App Insights failed-requests + p95 for N minutes; manual approval gate in Prod) → 100% → deactivate revisions older than the previous one (keep exactly one fallback for 24h).
- Scale rules (per env params): HTTP concurrency-based, min/max replicas per 02 §matrix. Outbox relay + Hangfire run in every replica by design (single-execution guaranteed at the storage layer, not the replica count).

## Static Web Apps deployment

- 3 apps (internal / portal / kiosk), each: build via `pnpm nx build <app>` per environment (build-time `VITE_API_BASE`/App Insights key; FE builds are per-env by design — see drawio page "FE Deploy Flow"), deploy via the SWA pipeline task with deployment token from Key Vault (or SWA deployment-token-less OIDC when available).
- Prod uses SWA **staging environment → swap**; Dev/SIT deploy direct.
- PR previews: SWA named preview environments created by the PR pipeline, pointed at the `pr-<id>` API base URL via build-time env (`VITE_API_BASE`).
- SPA fallback + API base URL are build-time config; no secrets in FE bundles, ever.

## Service connections & pipeline identities

- One ADO **service connection per environment** (workload identity federation to an Entra app federated with the ADO issuer) scoped to that env's resource group — Dev/SIT auto, Prod connection usable only from the `prod` ADO Environment (approval-guarded).
- ACR push: the Package stage uses a dedicated ACR-scoped connection.
- SonarQube token, Renovate PAT: ADO service connections / Library variable groups backed by Key Vault — no plaintext variables.
