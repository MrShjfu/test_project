# 06 — Alternative Option: Terraform instead of Bicep

**Status:** documented alternative. The standing decision is **Bicep** ([01 §IaC decision](01-cicd-architecture.md)). This doc answers "what would the same design look like on Terraform" so the team can compare concretely and switch later at a known cost. Everything outside the IaC layer (pipeline stages, blue-green, migration bundles, monitoring, environments) is **unchanged** — only the infra-provisioning steps swap.

## Repo layout

```
infra-tf/
├── modules/                        # reusable building blocks
│   ├── container-app/              # ACA env + app (mirrors modules/container-apps.bicep)
│   ├── postgres/                   # Flexible Server + db
│   ├── servicebus/                 # namespace + helm-events topic
│   ├── keyvault/
│   ├── static-web-app/             # ×3 via for_each
│   ├── monitoring/                 # Log Analytics + App Insights + alert rules
│   └── entra-app/                  # ★ azuread provider — app registrations (Bicep's weak spot)
├── envs/                           # DIRECTORY-PER-ENV (not workspaces — explicit > implicit)
│   ├── dev/    main.tf backend.tf terraform.tfvars
│   ├── sit/    …
│   └── prod/   …
└── versions.tf                     # pinned terraform + provider versions
```

- **Directory-per-env** instead of workspaces: each env has its own state, backend key and `tfvars` — easier to read, easier to RBAC, and no accidental `apply` against the wrong workspace. `terraform.tfvars` per env plays the role of `*.bicepparam` (SKUs, replicas, HA, retention — same matrix as [02](02-environments.md)).
- Providers: `azurerm` (primary), `azuread` (Entra app registrations — where Terraform is **stronger** than Bicep; replaces the Graph-Bicep spike in CICD-19/21), `azapi` (escape hatch for new ACA features `azurerm` hasn't caught up with).

## State management (the part Bicep doesn't have — the main cost of this option)

| Concern | Approach |
| --- | --- |
| Backend | Dedicated Azure Storage account (`rg-helm-tfstate` / `sthelmtfstate`), container `tfstate`, key `helm-<env>.tfstate` |
| Locking | Blob lease (built into the azurerm backend) — blocks concurrent applies |
| State security | State contains secrets/connection strings in plaintext ⇒ minimal RBAC (pipeline SP + break-glass only), versioning + soft delete enabled, no casual `terraform state pull` |
| Bootstrap | One-time script (chicken-and-egg: the first storage account is created with the `az` CLI or a mini Bicep file) |
| Subscription restructure | ⚠️ Known pain point: resource IDs in state are bound to the subscription — moving subscriptions means mass `terraform state mv`/`import` or re-provisioning. This is the main reason Bicep is the standing choice (01 §IaC) |

## What changes in the pipeline

The stage skeleton stays the same (CI → Package → Dev → IntTests → SIT → approval → Prod); the IaC portion of each deploy stage changes from `what-if → apply` to:

```
DeployX (per env):
  terraform -chdir=infra-tf/envs/<env> init      # backend config from pipeline vars
  terraform validate + fmt -check (+ tflint)      # in the CI stage
  terraform plan -out=tfplan                      # plan file published as an ARTIFACT
  [Prod] plan artifact attached to approval gate  # replaces the what-if diff — TF plans read better
  terraform apply tfplan                          # apply exactly the approved plan (no re-plan)
```

- **Plan-then-apply-the-plan** is Terraform's real advantage in a pipeline: the Prod approver reviews exactly the `tfplan` that will be applied — there is no "diff at approval time ≠ diff at apply time" gap (Bicep's what-if has that gap in theory).
- Drift check (CICD-22): scheduled `terraform plan -detailed-exitcode` — exit code 2 = drift → alert. Cleaner than what-if (less noise).
- ADO tasks: `TerraformTaskV4` or plain CLI via `AzureCLI@2` (plain CLI recommended — fewer marketplace-extension dependencies).
- OIDC service connections stay unchanged (`azurerm` supports workload identity federation via `use_oidc = true`).

## Backlog delta (if Terraform is chosen, these tasks change)

| Task | Bicep (current) | Terraform (this option) |
| --- | --- | --- |
| CICD-11 | `infra/params/*.bicepparam` + extend modules | Author `infra-tf/` modules + `envs/*/tfvars` (port from the Bicep skeleton — effort L instead of M) |
| CICD-11b (new) | — | Bootstrap state backend + RBAC + versioning (S) |
| CICD-15 | monitoring.bicep | `monitoring/` module (equivalent) |
| CICD-19/21 | Graph-Bicep spike, fallback `az ad` scripts | **Spike removed** — first-class `azuread` provider (the biggest win of this option) |
| CICD-20/23 | what-if artifact at approval | `tfplan` artifact at approval (better) |
| CICD-22 | what-if drift check | `plan -detailed-exitcode` (better) |
| existing `infra/` | keep | archive after the port completes (never run two IaC tools against the same resource group — hard rule) |

## When to switch to this option

Reopen the decision if at least one of these becomes true:

1. NTG/Scopic standardizes on Terraform organization-wide (skills + module registry available).
2. IaC needs to manage non-Azure providers (Datadog, Cloudflare, a Git host org, or Entra at scale).
3. Strict drift-detection / plan-review becomes a compliance requirement (plan-then-apply-the-plan + detailed-exitcode beats what-if).

Conversely: if the subscription restructure happens first, stay on Bicep through it and reconsider afterwards (avoids state surgery).

## Switching cost (estimate)

Porting the 6 existing Bicep modules (~500 lines, compiling clean) to HCL + state bootstrap + pipeline task changes: **~3–5 engineer-days**, plus the HCL learning curve if the team hasn't used it. No downtime (the port is provisioning-layer only), but if the switch happens after real infrastructure exists, add ~1–2 days for `terraform import` + state reconciliation.
