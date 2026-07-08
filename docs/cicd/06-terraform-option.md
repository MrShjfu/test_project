# 06 — Alternative Option: Terraform instead of Bicep

**Status:** documented alternative. The standing decision is **Bicep** ([01 §IaC decision](01-cicd-architecture.md)). This doc answers "what would the same design look like on Terraform" so the team can compare concretely and switch later with a known cost. Everything outside the IaC layer (pipeline stages, blue-green, migration bundles, monitoring, environments) is **unchanged** — only the infra-provisioning steps swap.

## Repo layout

```
infra-tf/
├── modules/                        # reusable, provider-agnostic building blocks
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

- **Directory-per-env** thay vì workspaces: mỗi env một state, một backend key, một `tfvars` — dễ đọc, dễ RBAC, không lỡ tay `apply` nhầm workspace. `terraform.tfvars` per env đóng vai trò của `*.bicepparam` (SKU, replicas, HA, retention — đúng ma trận [02](02-environments.md)).
- Providers: `azurerm` (chính), `azuread` (Entra app registrations — điểm Terraform **mạnh hơn** Bicep, thay thế spike Graph-Bicep của CICD-19/21), `azapi` (escape hatch cho tính năng ACA mới mà azurerm chưa kịp hỗ trợ).

## State management (phần Bicep không có — chi phí chính của option này)

| Việc | Cách làm |
| --- | --- |
| Backend | Azure Storage account riêng (`rg-helm-tfstate` / `sthelmtfstate`), container `tfstate`, key `helm-<env>.tfstate` |
| Locking | Blob lease (azurerm backend có sẵn) — chặn 2 apply song song |
| Bảo mật state | State chứa secrets/connection strings dạng plaintext ⇒ RBAC tối thiểu (chỉ pipeline SP + break-glass), versioning + soft delete bật, không ai `terraform state pull` tùy tiện |
| Khởi tạo | Bootstrap script một lần (chicken-and-egg: cái storage account đầu tiên tạo bằng `az` CLI hoặc Bicep mini) |
| Restructure subscription | ⚠️ Điểm đau đã biết: resource ID trong state gắn subscription — chuyển subscription = `terraform state mv`/`import` hàng loạt hoặc re-provision. Đây là lý do chính Bicep đang được chọn (01 §IaC) |

## Pipeline thay đổi thế nào

Các stage giữ nguyên khung (CI → Package → Dev → IntTests → SIT → approval → Prod); phần IaC trong mỗi deploy stage đổi từ `what-if → apply` sang:

```
DeployX (per env):
  terraform -chdir=infra-tf/envs/<env> init      # backend config từ pipeline vars
  terraform validate + fmt -check (+ tflint)      # ở CI stage
  terraform plan -out=tfplan                      # plan file publish làm ARTIFACT
  [Prod] plan artifact đính vào approval gate     # thay cho what-if diff — plan của TF đọc dễ hơn what-if
  terraform apply tfplan                          # apply đúng plan đã duyệt (không plan lại)
```

- **Plan-then-apply-the-plan** là lợi thế thật của Terraform trong pipeline: Prod approver duyệt chính xác `tfplan` sẽ được apply, không có khoảng hở "diff lúc duyệt ≠ diff lúc apply" (what-if của Bicep có khoảng hở này về lý thuyết).
- Drift check (CICD-22): scheduled `terraform plan -detailed-exitcode` — exit 2 = drift → alert. Chuẩn hơn what-if (ít noise).
- Task ADO: dùng `TerraformTaskV4`/CLI thuần qua `AzureCLI@2` (khuyến nghị CLI thuần — ít phụ thuộc extension marketplace).
- Service connection OIDC giữ nguyên (azurerm hỗ trợ workload identity federation qua `use_oidc = true`).

## Backlog delta (nếu chọn Terraform, các task đổi như sau)

| Task | Bicep (hiện tại) | Terraform (option này) |
| --- | --- | --- |
| CICD-11 | `infra/params/*.bicepparam` + mở rộng modules | Viết `infra-tf/` modules + `envs/*/tfvars` (port từ Bicep skeleton — effort L thay vì M) |
| CICD-11b (mới) | — | Bootstrap state backend + RBAC + versioning (S) |
| CICD-15 | monitoring.bicep | module `monitoring/` (tương đương) |
| CICD-19/21 | Graph Bicep spike, fallback `az ad` scripts | **Bỏ spike** — dùng provider `azuread` chính thống (điểm cộng lớn nhất của option) |
| CICD-20/23 | what-if artifact ở approval | `tfplan` artifact ở approval (tốt hơn) |
| CICD-22 | what-if drift check | `plan -detailed-exitcode` (tốt hơn) |
| infra/ hiện có | giữ | archive sau khi port xong (không chạy song song 2 IaC trên cùng RG — cấm tuyệt đối) |

## Khi nào nên chuyển sang option này

Kích hoạt lại quyết định nếu ≥1 điều sau thành sự thật:

1. NTG/Scopic chuẩn hóa Terraform toàn tổ chức (kỹ năng + module registry có sẵn).
2. Cần IaC quản lý provider ngoài Azure (Datadog, Cloudflare, GitHub org…, hoặc Entra ở quy mô lớn).
3. Nhu cầu drift-detection/plan-review nghiêm ngặt trở thành yêu cầu compliance (plan-then-apply-the-plan + detailed-exitcode thắng what-if).

Ngược lại, nếu subscription restructure diễn ra trước khi chuyển — ở lại Bicep qua đợt đó rồi hẵng cân nhắc (tránh state surgery).

## Chi phí chuyển đổi (ước lượng)

Port 6 module Bicep hiện có (~500 dòng, đã compile sạch) sang HCL + state bootstrap + pipeline đổi task: **~3–5 ngày** một kỹ sư, cộng learning curve nếu team chưa quen HCL. Không có downtime (port là provision-layer, không đụng app), nhưng cần một lần `terraform import` nếu port sau khi hạ tầng thật đã tồn tại (thêm ~1–2 ngày cho import + reconcile).
