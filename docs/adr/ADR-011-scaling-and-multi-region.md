<!-- Source: https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM/pages/2360024/ADR-011+Scaling+Model+Multi-Region+Posture+Active-Passive+DR (v3, exported 2026-07-06) -->

# ADR-011: Scaling Model & Multi-Region Posture (Active-Passive DR)

Status: Ready for ReviewYellow

| **Attribute** | **Value** |
| --- | --- |
| **Proposed by** | Vu Lam · |
| **Contributors** | Vu Lam |
| **Approved by** | — · pending |
| **Links** | [Architecture Overview §12](../architecture/overview.md), [ADR-001](ADR-001-modular-monolith.md), [ADR-004](ADR-004-async-messaging-and-outbox.md), [ADR-005](ADR-005-background-jobs-hangfire.md), [ADR-010](ADR-010-hosting-azure-container-apps.md) |

---

## Context

Stakeholders asked how the platform scales (autoscale) and whether it can run multi-cluster / multi-datacentre. The architecture must answer this without prematurely distributing the system. Key facts:

- The backend is a modular monolith on Azure Container Apps (ADR-010), scaled horizontally as one unit.
- Azure Database for PostgreSQL is single-writer (one primary) — the binding constraint on multi-region writes.
- Background work (outbox relay per ADR-004, scheduled jobs per ADR-005) must run **once**, not once per autoscaled replica.
- NTG’s load is modest (tens–hundreds of users); the business is B2B sailmaking with EU licensees (potential data-residency considerations).

The risk is over-engineering (active-active multi-master) or under-planning (a stateful app that can’t scale out or fail over).

## Decision

**1. Horizontal autoscale, stateless app.** Scale the whole monolith out via ACA + KEDA (HTTP concurrency / CPU / custom metrics). The app tier holds no unique per-replica state: sessions → Redis (or stateless JWT), files → Blob Storage, shared caches → Redis, background work → single-execution workers.

**2. Single-execution background jobs.** The outbox relay and scheduled jobs run exactly once across replicas — via **Hangfire** storage locks (ADR-005) and/or a dedicated single-instance **worker app** running off the same image (HTTP `api` app scales independently of the `worker` app).

**3. Multi-region target = active-passive (DR), not active-active.** Design for disaster recovery now, build when warranted: stateless app deployable to a second region; **PostgreSQL geo-replication** (read-only replica, promoted on failover); **geo-redundant (GRS) Blob Storage**; **Azure Front Door** for global routing + health-based failover, placed in front even while single-region so adding region 2 is configuration, not re-architecture.

**4. Data residency decided early.** If EU licensees require in-region data, partition by `company_id`/region. This is the one scaling decision that is expensive to retrofit; everything else is incremental.

Multi-*cluster* (multiple ACA environments) is used only as the per-region step above, or for hard per-company isolation if ever mandated — not within a single region. Independent per-module scaling is explicitly out of scope until a module is extracted (ADR-001 triggers).

## Rationale

A modular monolith does not limit scaling — a stateless app tier does the work, and the genuinely hard parts are the database and background jobs, not the monolith shape. Scaling the whole monolith as a unit is more than sufficient at NTG’s scale; you reach 2–10 replicas long before any single module needs isolation, and that point is precisely an extraction trigger.

Active-active is rejected because PostgreSQL’s single-writer model would force either regional sharding or multi-master conflict resolution — major complexity with no business case for a B2B sailmaker. Active-passive DR delivers resilience at moderate cost, and the only thing that must be decided up front (data residency / partitioning) is called out so it isn’t retrofitted painfully later.

## Consequences

**Good:**

- Clear, incremental scaling ladder; nothing premature.
- Resilience path (DR) defined and cheap to plan now (Front Door in front from day 1).
- Background-job correctness under autoscale is explicit, not assumed.
- `company_id` already in the data model gives a natural partition axis if residency or isolation is ever required.

**Bad / watch out for:**

- Whole-monolith scaling means no per-module scaling until extraction — acceptable at current scale.
- Active-passive DR has non-zero RPO/RTO (replica lag + promotion time) — document the failover runbook.
- Stateless discipline must be enforced (no in-memory session/file state) or scale-out breaks subtly.
- Deferring the residency decision past day 1 risks an expensive partition retrofit — decide before Phase 1 data design.

## Alternatives Considered

- **Active-active multi-region** — rejected: PostgreSQL single-writer makes it require sharding or multi-master conflict handling; very high complexity, no business case for NTG.
- **Per-module independent scaling within the monolith** — rejected: not possible without extraction; whole-monolith scale-out suffices at current load (this is an ADR-001 extraction trigger when it changes).
- **Run background jobs on every replica** — rejected: causes duplicate processing (double outbox publish, duplicate scheduled jobs); single-execution via Hangfire/worker app is mandatory.
- **No multi-region planning until needed** — rejected: stateless design and Front Door placement are near-free now and painful to retrofit; DR readiness is cheap insurance.
