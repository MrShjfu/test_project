# 04 — Monitoring & Alerting

Diagram: environment/topology view including the monitoring plane: [diagrams/environments-monitoring.drawio](diagrams/environments-monitoring.drawio).

## Stack

| Piece | Choice | Notes |
| --- | --- | --- |
| APM / traces | **Application Insights** (workspace-based), one per env | .NET auto-instrumentation on Helm.Host; JS SDK in the 3 FE apps; W3C `traceparent` end-to-end (overview §11) |
| Logs | **Log Analytics workspace** per env | ACA console+system logs, Postgres logs, pipeline diagnostic streams |
| Metrics/alerts | **Azure Monitor** alert rules (Bicep `monitoring.bicep`) | Action group → email + Teams webhook |
| Dashboards | Azure **Workbooks**, one per env | RED (Rate/Errors/Duration) per module via App Insights custom dimensions |
| Uptime | App Insights **availability test** (Prod, `/health`) | 5-min ping, multi-region probes |

Custom telemetry that the code already emits or must emit (tracked in backlog CICD-16): saga state transitions (when sagas land), outbox lag (oldest unprocessed row age), DLQ depth, Hangfire failed count, `AUDIT cross-company` events (security signal — route to a dedicated log-based alert).

## Alert catalog (Phase 1 minimum)

| Alert | Signal | Threshold (initial) | Sev |
| --- | --- | --- | --- |
| API latency | App Insights p95 server response | > 500 ms for 10 min | 2 |
| Error rate | Failed requests % | > 1% for 5 min | 1 |
| Health probe | Availability test | 2 consecutive failures | 1 |
| Outbox stuck | Custom metric: oldest unprocessed outbox row | > 5 min (ADR-004 runbook) | 1 |
| DLQ depth | Service Bus dead-letter count | > 0 (prod) | 2 |
| Hangfire failures | `hangfire` failed set growth | > 0 new in 15 min | 2 |
| Postgres | CPU > 80% / storage > 80% / connections near max | 15 min | 2 |
| Cross-company audit spike | Log query on AUDIT events | > baseline (tune later) | 3 (investigate) |
| Cert/quota | SWA/ACA platform advisories | any | 3 |

Thresholds start from the NFR table (overview §14) and get tuned with real traffic; the alert **catalog** is code (Bicep), so tuning is a PR, not a portal click.

## Wiring plan

1. `infra/modules/monitoring.bicep`: Log Analytics + App Insights + action group + the alert rules above, outputs the connection string consumed by the ACA module (CICD-15).
2. Helm.Host: add App Insights SDK registration (connection string from config; no-op locally when unset) + custom metrics for outbox lag/DLQ/Hangfire via a small `IHostedService` sampler (CICD-16).
3. FE apps: App Insights JS snippet behind a build-time flag (off locally/PR) (CICD-17).
4. Workbook JSON checked into `infra/workbooks/` and deployed by Bicep (CICD-18).

## What we deliberately do NOT add in Phase 1

Grafana/Prometheus stack (App Insights covers the need at this scale), OpenTelemetry collector self-hosting (App Insights exporter is enough; OTel API already used by .NET), synthetic user journeys beyond the health availability test, on-call rotation tooling (team of 2–3 — Teams alerts suffice; revisit at go-live).
